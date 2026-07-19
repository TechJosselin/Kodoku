using System;
using System.Linq;
using Kodoku.Items;
using Kodoku.Items.Inventory;
using Kodoku.Player;

namespace Kodoku.World.Containers;

/// <summary>
/// Conteneur du monde partagé, consultable par plusieurs joueurs simultanément — composant monde
/// autonome (Approche A, voir docs/architecture/WORLD_CONTAINER_ARCHITECTURE.md, section 5), aucune
/// dépendance structurelle à <see cref="Kodoku.Player.Inventory.PlayerInventoryComponent"/>. Porte un
/// <see cref="InventoryContainer"/> canonique host-only (même choix que
/// <see cref="Kodoku.Player.Inventory.PlayerInventoryComponent.Container"/>), une liste host-only de
/// viewers (<c>Connection</c>, jamais <c>[Sync]</c>) et une révision de contenu qui ne change jamais
/// à l'ouverture/fermeture/resync — seulement lors d'une future mutation réelle (voir
/// <see cref="NotifyContentMutated"/>, préparé mais jamais appelé dans cette V1 : aucun transfert
/// n'existe encore).
///
/// Le <c>GameObject</c> qui porte ce composant n'a jamais de <c>Network Owner</c> joueur — l'autorité
/// de mutation reste déterminée par <see cref="Sandbox.Networking.IsHost"/>, jamais par ownership
/// (ADR-0006). L'identité du conteneur est portée par ce composant/GameObject lui-même ; aucun
/// <c>StableContainerId</c> n'est introduit dans cette V1 (réservé pour une future sauvegarde, voir
/// docs/architecture/WORLD_CONTAINER_ARCHITECTURE.md, section 13).
///
/// Transport réseau : broadcast filtré (<c>[Rpc.Broadcast]</c> + <c>Rpc.FilterInclude</c>), validé par
/// test runtime réel (Spike S0, voir docs/research/WORLD_CONTAINER_MULTIVIEWER_SPIKE.md et
/// ADR-0006) — jamais <c>[Rpc.Owner]</c> (pas de propriétaire unique) ni <c>[Sync(FromHost)]</c> public
/// (le contenu reste privé aux viewers actuels).
/// </summary>
public sealed class WorldContainerComponent : Component, Component.INetworkListener
{
	[Group( "Configuration" )]
	[Property]
	public int Width { get; set; } = 4;

	[Group( "Configuration" )]
	[Property]
	public int Height { get; set; } = 4;

	/// <summary>
	/// Borne haute propre à ce conteneur — même patron que
	/// <see cref="Kodoku.Items.Interaction.WorldItemPickupComponent.MaxPickupDistance"/>. La portée
	/// réellement appliquée côté host est <c>MathF.Min(pawn.PlayerController.ReachLength, MaxOpenDistance)</c>
	/// (voir <see cref="IsWithinRange"/>) — jamais cette valeur seule.
	/// </summary>
	[Group( "Configuration" )]
	[Property]
	public float MaxOpenDistance { get; set; } = 150f;

	/// <summary>
	/// Null sur toute instance qui n'est pas celle du host — même garantie que
	/// <see cref="Kodoku.Player.Inventory.PlayerInventoryComponent.Container"/>. Un appelant doit
	/// toujours vérifier <see cref="Sandbox.Networking.IsHost"/> avant de muter ce conteneur.
	/// </summary>
	public InventoryContainer Container { get; private set; }

	/// <summary>
	/// Révision host-authoritative du contenu — incrémentée uniquement par
	/// <see cref="NotifyContentMutated"/>, jamais par l'ouverture, la fermeture, l'ajout/retrait d'un
	/// viewer, ou une resynchronisation (voir docs/architecture/WORLD_CONTAINER_ARCHITECTURE.md,
	/// section 13). Vaut toujours 0 sur une instance non-host.
	/// </summary>
	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public int HostRevision { get; private set; }

	/// <summary>
	/// Viewers actuellement autorisés — host-only, jamais <c>[Sync]</c>, jamais reconstruite depuis un
	/// cache client (ADR-0006). L'état « ouvert/fermé » du conteneur est dérivé de
	/// <c>_viewers.Count &gt; 0</c>, jamais un booléen séparé qui pourrait diverger.
	/// </summary>
	readonly HashSet<Connection> _viewers = new();

	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public int ViewerCount => _viewers.Count;

	/// <summary>Résumé lisible du contenu, pour inspection en éditeur côté host — mêmes réserves que <see cref="Kodoku.Player.Inventory.PlayerInventoryComponent.Contents"/>.</summary>
	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public string Contents => Container is null
		? ""
		: string.Join( ", ", Container.Placements.Select( p => $"{p.Item.Definition.DisplayName} x{p.Item.Quantity}" ) );

	/// <summary>
	/// Cache local de présentation — présent uniquement chez un viewer actif, jamais une preuve
	/// d'autorisation (même garantie que <see cref="Kodoku.Player.Inventory.PlayerInventoryComponent.LocalEntries"/>).
	/// </summary>
	public IReadOnlyList<WorldContainerSnapshotEntry> LocalEntries { get; private set; } = Array.Empty<WorldContainerSnapshotEntry>();

	/// <summary>Révision du dernier snapshot accepté. -1 tant qu'aucun n'a été reçu, ou après fermeture/invalidation.</summary>
	public int LocalRevision { get; private set; } = -1;

	public int LocalWidth { get; private set; }

	public int LocalHeight { get; private set; }

	/// <summary>Dérivé du dernier snapshot/invalidation reçu — jamais transmis comme champ séparé du snapshot lui-même.</summary>
	public bool IsLocalSessionOpen { get; private set; }

	/// <summary>Raison de la dernière invalidation reçue (vide si aucune) — présentation uniquement.</summary>
	public string LocalInvalidationReason { get; private set; } = "";

	protected override void OnStart()
	{
		// Même choix que PlayerInventoryComponent/WorldItemComponent : Networking.IsHost, pas IsProxy.
		if ( Networking.IsHost )
		{
			Container = new InventoryContainer( Width, Height );
			Log.Info( $"[WorldContainer][Initialized] '{GameObject.Name}' : {Width}x{Height}." );
		}
	}

	// --- Ouverture ---

	/// <summary>
	/// Point d'entrée réseau unique de l'ouverture. <c>[Rpc.Host]</c> résout <see cref="Rpc.Caller"/> —
	/// aucun paramètre transmis, la cible est implicite (ce <c>GameObject</c> précis).
	/// </summary>
	[Rpc.Host]
	public void RequestOpen()
	{
		var caller = Rpc.Caller;
		var result = TryOpenAuthoritative( caller );

		if ( result.Success )
			Log.Info( $"[WorldContainer][Open][Success] '{caller?.DisplayName}' -> '{GameObject.Name}'. ViewerCount={_viewers.Count}." );
		else
			Log.Warning( $"[WorldContainer][Open][Fail] '{caller?.DisplayName}' -> '{GameObject.Name}': {result.FailureReason}" );
	}

	/// <summary>
	/// Transaction métier complète, indépendante de tout transport réseau — même esprit que
	/// <see cref="Kodoku.Items.Interaction.WorldItemPickupComponent.TryPickupAuthoritative"/>. Une
	/// seconde ouverture par le même viewer est un succès idempotent (pas de doublon dans
	/// <c>_viewers</c>, un nouveau snapshot est simplement renvoyé) — pas de raison d'échec dédiée à ce
	/// cas (voir docs/architecture/WORLD_CONTAINER_ARCHITECTURE.md, section 3 : multi-viewer autorisé
	/// par design, rien n'interdit une seconde ouverture par le même joueur).
	/// </summary>
	public WorldContainerOperationResult TryOpenAuthoritative( Connection requester )
	{
		if ( !Networking.IsHost )
		{
			Log.Error( $"[WorldContainer][InternalError] TryOpenAuthoritative() exécuté hors du host pour '{GameObject.Name}'." );
			return WorldContainerOperationResult.Fail( WorldContainerFailureReason.InternalError );
		}

		if ( Container is null || !GameObject.IsValid() )
			return WorldContainerOperationResult.Fail( WorldContainerFailureReason.ContainerUnavailable );

		var pawn = KodokuPlayerComponent.FindByConnection( Scene, requester );
		if ( pawn is null || pawn.PlayerController is null )
			return WorldContainerOperationResult.Fail( WorldContainerFailureReason.InvalidCaller );

		if ( !IsWithinRange( pawn ) )
			return WorldContainerOperationResult.Fail( WorldContainerFailureReason.OutOfRange );

		if ( _viewers.Add( requester ) )
			Log.Info( $"[WorldContainer][ViewerAdded] '{requester?.DisplayName}' -> '{GameObject.Name}'. Total={_viewers.Count}." );

		// Snapshot initial envoyé au sein de ce même appel host — jamais depuis un handler séparé
		// déclenché plus tard (contrainte anti-chaînage RPC, voir MULTIPLAYER_ARCHITECTURE.md).
		SendSnapshotTo( requester );

		return WorldContainerOperationResult.Ok();
	}

	// --- Fermeture ---

	/// <summary>Point d'entrée réseau unique de la fermeture volontaire. Aucun paramètre.</summary>
	[Rpc.Host]
	public void RequestClose()
	{
		var caller = Rpc.Caller;
		var result = TryCloseAuthoritative( caller );

		if ( result.Success )
			Log.Info( $"[WorldContainer][Close] '{caller?.DisplayName}' -> '{GameObject.Name}'. ViewerCount={_viewers.Count}." );
		else
			Log.Warning( $"[WorldContainer][Close] '{caller?.DisplayName}' -> '{GameObject.Name}': {result.FailureReason} (idempotent, aucune action)." );
	}

	/// <summary>
	/// Sûre si l'appelant n'est déjà plus viewer (idempotence). Ordre validé par test runtime réel
	/// (Spike S0-D) : invalidation ciblée d'abord, retrait de la collection ensuite — jamais l'inverse.
	/// Ne modifie jamais <see cref="Container"/> ni <see cref="HostRevision"/>.
	/// </summary>
	public WorldContainerOperationResult TryCloseAuthoritative( Connection requester )
	{
		if ( !Networking.IsHost )
		{
			Log.Error( $"[WorldContainer][InternalError] TryCloseAuthoritative() exécuté hors du host pour '{GameObject.Name}'." );
			return WorldContainerOperationResult.Fail( WorldContainerFailureReason.InternalError );
		}

		if ( !_viewers.Contains( requester ) )
			return WorldContainerOperationResult.Fail( WorldContainerFailureReason.NotViewer );

		SendInvalidationTo( requester, "closed" );
		_viewers.Remove( requester );
		Log.Info( $"[WorldContainer][ViewerRemoved] '{requester?.DisplayName}' -> '{GameObject.Name}'. Total={_viewers.Count}." );

		return WorldContainerOperationResult.Ok();
	}

	// --- Resynchronisation ---

	/// <summary>Point d'entrée réseau unique de la resynchronisation explicite. Aucun paramètre.</summary>
	[Rpc.Host]
	public void RequestSnapshot()
	{
		var caller = Rpc.Caller;
		var result = TryResyncAuthoritative( caller );

		if ( result.Success )
			Log.Info( $"[WorldContainer][Snapshot][Send] Resync '{caller?.DisplayName}' <- '{GameObject.Name}'." );
		else
			Log.Warning( $"[WorldContainer][Snapshot][Ignored] Resync '{caller?.DisplayName}' -> '{GameObject.Name}': {result.FailureReason}" );
	}

	/// <summary>
	/// Revalide l'appartenance aux viewers puis la distance à chaque appel — jamais seulement à
	/// l'ouverture. Un rejet de distance invalide la session côté host (retrait immédiat) plutôt que
	/// de laisser un viewer hors de portée considéré comme valide pour une tentative suivante (voir
	/// docs/architecture/WORLD_CONTAINER_ARCHITECTURE.md, section 8). N'incrémente jamais
	/// <see cref="HostRevision"/> — une resync renvoie l'état déjà existant tel quel.
	/// </summary>
	public WorldContainerOperationResult TryResyncAuthoritative( Connection requester )
	{
		if ( !Networking.IsHost )
		{
			Log.Error( $"[WorldContainer][InternalError] TryResyncAuthoritative() exécuté hors du host pour '{GameObject.Name}'." );
			return WorldContainerOperationResult.Fail( WorldContainerFailureReason.InternalError );
		}

		if ( Container is null )
			return WorldContainerOperationResult.Fail( WorldContainerFailureReason.ContainerUnavailable );

		if ( !_viewers.Contains( requester ) )
			return WorldContainerOperationResult.Fail( WorldContainerFailureReason.NotViewer );

		var pawn = KodokuPlayerComponent.FindByConnection( Scene, requester );
		if ( pawn is null || pawn.PlayerController is null )
		{
			SendInvalidationTo( requester, "invalid caller" );
			_viewers.Remove( requester );
			Log.Info( $"[WorldContainer][ViewerRemoved] '{requester?.DisplayName}' -> '{GameObject.Name}' (invalid caller on resync). Total={_viewers.Count}." );
			return WorldContainerOperationResult.Fail( WorldContainerFailureReason.InvalidCaller );
		}

		if ( !IsWithinRange( pawn ) )
		{
			SendInvalidationTo( requester, "out of range" );
			_viewers.Remove( requester );
			Log.Info( $"[WorldContainer][ViewerRemoved] '{requester?.DisplayName}' -> '{GameObject.Name}' (out of range on resync). Total={_viewers.Count}." );
			return WorldContainerOperationResult.Fail( WorldContainerFailureReason.OutOfRange );
		}

		SendSnapshotTo( requester );
		return WorldContainerOperationResult.Ok();
	}

	// --- Mutation (préparé, jamais appelé dans cette V1 — aucun transfert n'existe encore) ---

	/// <summary>
	/// Point d'entrée host-only pour une future mutation réelle du contenu (transferts, hors périmètre
	/// de cette V1). Incrémente <see cref="HostRevision"/> exactement une fois puis diffuse un snapshot
	/// complet à tous les viewers actuels. N'est appelée depuis aucun flux de session dans cette V1.
	/// </summary>
	public void NotifyContentMutated()
	{
		if ( !Networking.IsHost )
		{
			Log.Error( $"[WorldContainer][InternalError] NotifyContentMutated() appelé hors du host pour '{GameObject.Name}'." );
			return;
		}

		HostRevision++;
		SendSnapshotToAllViewers();
	}

	// --- Transport ---

	/// <summary>
	/// Distance effective = <c>MathF.Min(pawn.PlayerController.ReachLength, MaxOpenDistance)</c> — même
	/// principe que le pickup, une RPC directe ne bénéficie jamais d'une portée supérieure à
	/// l'interaction stock. Pas de validation de ligne de vue : aucune primitive réutilisable n'existe
	/// pour ce besoin en dehors de la logique de trace déjà spécifique au pickup (voir
	/// docs/architecture/WORLD_CONTAINER_CORE_TESTS.md pour la note de portée de cette V1).
	/// </summary>
	bool IsWithinRange( KodokuPlayerComponent pawn )
	{
		var eyePosition = pawn.PlayerController.EyePosition;
		var containerPosition = GameObject.WorldPosition;
		var effectiveRange = MathF.Min( pawn.PlayerController.ReachLength, MaxOpenDistance );

		return Vector3.DistanceBetween( eyePosition, containerPosition ) <= effectiveRange;
	}

	/// <summary>Filet de sécurité léger avant chaque envoi — <c>OnDisconnected</c> reste la source principale du nettoyage (ADR-0006).</summary>
	void PurgeDisconnectedViewers()
	{
		_viewers.RemoveWhere( c => c is null || !c.IsActive );
	}

	WorldContainerSnapshotEntry[] BuildSnapshotEntries()
	{
		return Container.Placements
			.Select( p => new WorldContainerSnapshotEntry( p.InstanceId.ToString(), p.Item.Definition.ItemId, p.Item.Quantity, p.X, p.Y, p.IsRotated ) )
			.ToArray();
	}

	/// <summary>Snapshot ciblé à un seul viewer — ouverture, resynchronisation individuelle.</summary>
	void SendSnapshotTo( Connection target )
	{
		if ( Container is null )
			return;

		PurgeDisconnectedViewers();

		var entries = BuildSnapshotEntries();
		Log.Info( $"[WorldContainer][Snapshot][Send] '{GameObject.Name}' -> '{target?.DisplayName}' : revision={HostRevision}, entries={entries.Length}, dims={Width}x{Height}." );

		using ( Rpc.FilterInclude( target ) )
		{
			ReceiveSnapshot( HostRevision, Width, Height, entries );
		}
	}

	/// <summary>
	/// Snapshot diffusé à tous les viewers courants — utilisé après une mutation réelle (voir
	/// <see cref="NotifyContentMutated"/>). Garde <c>ViewerCount == 0</c> pour éviter un appel RPC
	/// inutile — optimisation, pas une condition de correction (collection vide déjà sûre, S0-A).
	/// </summary>
	void SendSnapshotToAllViewers()
	{
		if ( Container is null )
			return;

		PurgeDisconnectedViewers();

		if ( _viewers.Count == 0 )
			return;

		var entries = BuildSnapshotEntries();
		Log.Info( $"[WorldContainer][Snapshot][Send] '{GameObject.Name}' -> all viewers : revision={HostRevision}, entries={entries.Length}, dims={Width}x{Height}, viewers={_viewers.Count}." );

		using ( Rpc.FilterInclude( _viewers ) )
		{
			ReceiveSnapshot( HostRevision, Width, Height, entries );
		}
	}

	/// <summary>
	/// Reçoit un snapshot depuis le host — exécutée uniquement chez les viewers ciblés par le filtre
	/// (<c>[Rpc.Broadcast]</c> + <c>Rpc.FilterInclude</c>, voir ADR-0006). Un snapshot de révision
	/// strictement inférieure à <see cref="LocalRevision"/> est ignoré ; une révision égale est
	/// acceptée (nécessaire pour qu'une resynchronisation explicite reconstruise le cache même sans
	/// nouvelle mutation côté host).
	/// </summary>
	[Rpc.Broadcast]
	public void ReceiveSnapshot( int revision, int width, int height, WorldContainerSnapshotEntry[] entries )
	{
		if ( revision < LocalRevision )
		{
			Log.Warning( $"[WorldContainer][Snapshot][Ignored] '{GameObject.Name}' : reçu revision={revision}, déjà à revision={LocalRevision} — ignoré." );
			return;
		}

		LocalRevision = revision;
		LocalWidth = width;
		LocalHeight = height;
		LocalEntries = entries ?? Array.Empty<WorldContainerSnapshotEntry>();
		IsLocalSessionOpen = true;
		LocalInvalidationReason = "";

		Log.Info( $"[WorldContainer][Snapshot][Receive] '{GameObject.Name}' : revision={LocalRevision}, entries={LocalEntries.Count}, dims={LocalWidth}x{LocalHeight}." );
	}

	/// <summary>Invalidation ciblée à un seul viewer — fermeture, échec de revalidation lors d'une resync.</summary>
	void SendInvalidationTo( Connection target, string reason )
	{
		Log.Info( $"[WorldContainer][Invalidate] '{GameObject.Name}' -> '{target?.DisplayName}' : {reason}." );

		using ( Rpc.FilterInclude( target ) )
		{
			ReceiveInvalidate( reason );
		}
	}

	/// <summary>Vide le cache local et ferme la session côté client — reçue uniquement par le viewer ciblé.</summary>
	[Rpc.Broadcast]
	public void ReceiveInvalidate( string reason )
	{
		LocalEntries = Array.Empty<WorldContainerSnapshotEntry>();
		LocalRevision = -1;
		LocalWidth = 0;
		LocalHeight = 0;
		IsLocalSessionOpen = false;
		LocalInvalidationReason = reason ?? "";

		Log.Info( $"[WorldContainer][Invalidate] '{GameObject.Name}' : session invalidée ({LocalInvalidationReason}), cache local vidé." );
	}

	// --- Déconnexion ---

	/// <summary>
	/// Mécanisme principal de nettoyage à la déconnexion, confirmé par test runtime réel (Spike S0-G) —
	/// host-only par contrat de l'interface. La purge défensive de <see cref="PurgeDisconnectedViewers"/>
	/// reste un filet de sécurité complémentaire, pas la source principale du nettoyage.
	/// </summary>
	void Component.INetworkListener.OnDisconnected( Connection connection )
	{
		if ( _viewers.Remove( connection ) )
			Log.Info( $"[WorldContainer][DisconnectedCleanup] '{connection?.DisplayName}' retiré de '{GameObject.Name}'. Total={_viewers.Count}." );
	}
}
