using System;
using System.Linq;
using Kodoku.Items;
using Kodoku.Items.Inventory;
using Kodoku.Player;
using Kodoku.Player.Inventory;

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

	/// <summary>
	/// Cache local non autoritaire du dernier résultat de transfert reçu (voir
	/// <see cref="ReceiveTransferResult"/>) — accusé de traitement pour la présentation/les tests,
	/// jamais consulté par le host pour autoriser quoi que ce soit, jamais <c>[Sync]</c> (transmis
	/// une fois par <c>[Rpc.Broadcast]</c> filtré, pas un état répliqué continu). Ne contient aucune
	/// donnée d'inventaire — voir <see cref="WorldContainerTransferResult"/>.
	/// </summary>
	public bool HasLocalTransferResult { get; private set; }

	public WorldContainerTransferResult LastTransferResult { get; private set; }

	/// <summary>Incrémenté à chaque <see cref="ReceiveTransferResult"/> reçu — permet à l'UI/aux tests de détecter un nouveau résultat même si son contenu est identique au précédent.</summary>
	public int LocalTransferResultSequence { get; private set; }

	/// <summary>
	/// Référence locale (non networkée) vers le conteneur dont une session vient de s'ouvrir pour CE
	/// client — même esprit que <see cref="Kodoku.Player.KodokuPlayerComponent.Local"/> : statique,
	/// réévaluée par événements, jamais un lien de scène figé. Seul point d'entrée prévu pour une
	/// future UI de production qui doit savoir « quel conteneur dois-je afficher ? » sans avoir à
	/// interroger tous les <see cref="WorldContainerComponent"/> de la scène. Mise à jour uniquement
	/// depuis <see cref="ReceiveSnapshot"/> (ouverture)/<see cref="ReceiveInvalidate"/> (fermeture) —
	/// jamais depuis <see cref="Kodoku.World.Containers.WorldContainerInteractionComponent.Press"/>
	/// directement, qui ne fait que déclencher la requête : seule la confirmation réseau (snapshot
	/// reçu) prouve qu'une session est réellement ouverte côté host.
	/// </summary>
	public static WorldContainerComponent LocalOpenContainer { get; private set; }

	/// <summary>Déclenché quand <see cref="LocalOpenContainer"/> passe de fermé à ouvert pour ce conteneur précis.</summary>
	public static event Action<WorldContainerComponent> LocalContainerOpened;

	/// <summary>Déclenché quand <see cref="LocalOpenContainer"/> se ferme pour ce conteneur précis (volontaire, invalidation, ou destruction locale).</summary>
	public static event Action<WorldContainerComponent> LocalContainerClosed;

	/// <summary>
	/// Le conteneur le plus récemment demandé localement par CE client, TANT QUE cette demande n'a
	/// pas encore été confirmée — une intention en attente, jamais une confirmation, jamais un
	/// historique. Distinct de <see cref="LocalOpenContainer"/> (état confirmé par le host). Sert de
	/// filtre anti-réponse-retardée dans <see cref="ReceiveSnapshot"/> : si un snapshot d'ouverture
	/// arrive pour un conteneur qui n'est plus celui-ci, c'est la confirmation tardive d'un choix déjà
	/// abandonné (deux ouvertures rapprochées sur des conteneurs différents, réponses reçues dans un
	/// ordre non garanti) — ce conteneur ne doit alors jamais devenir <see cref="LocalOpenContainer"/>.
	///
	/// Assigné uniquement par <see cref="RequestLocalOpen"/>, nettoyé (via
	/// <see cref="ClearLocalRequestedContainerIfSelf"/>, toujours gardé par une comparaison à
	/// <c>this</c>) dès que la demande est consommée par un snapshot accepté, ou devient obsolète sans
	/// confirmation (invalidation, destruction).
	///
	/// Si le host refuse <see cref="RequestOpen"/> (distance, autorité, conteneur indisponible), aucune
	/// réponse n'est envoyée au client — ce champ peut rester bloqué sur un conteneur qui ne s'ouvrira
	/// jamais, jusqu'à la prochaine demande. Sans conséquence : les trois chemins d'échec de
	/// <see cref="TryOpenAuthoritative"/> retournent tous avant <c>_viewers.Add(requester)</c>, donc ce
	/// client n'est jamais ajouté comme viewer et ne peut structurellement jamais recevoir de snapshot
	/// pour ce conteneur.
	/// </summary>
	static WorldContainerComponent _localRequestedContainer;

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

	/// <summary>
	/// Point d'entrée local pour « le joueur veut consulter CE conteneur maintenant » — appelé depuis
	/// <see cref="Kodoku.World.Containers.WorldContainerInteractionComponent.Press"/>, jamais
	/// <see cref="RequestOpen"/> directement (qui reste le transport RPC pur). Invariant : un joueur
	/// local ne possède qu'un seul conteneur monde actif dans l'UI à un instant donné — les autres
	/// joueurs peuvent consulter le même conteneur en parallèle sans effet ici, cette coordination est
	/// strictement locale à CE client, jamais networkée.
	///
	/// Ferme immédiatement (<see cref="CloseLocalSessionImmediately"/>, sans attendre le réseau)
	/// l'ancien conteneur actif s'il diffère de la cible — garantit un ordre local déterministe
	/// (<see cref="LocalContainerClosed"/> pour l'ancien avant <see cref="LocalContainerOpened"/> pour
	/// le nouveau), puis enregistre l'intention (<see cref="_localRequestedContainer"/>) pour que
	/// <see cref="ReceiveSnapshot"/> puisse rejeter la confirmation tardive d'un choix déjà abandonné.
	///
	/// L'ordre local des événements est garanti (fermeture synchrone avant l'envoi réseau de la nouvelle
	/// demande). L'ordre de traitement du host entre les deux RPC sortantes (`RequestClose()` de
	/// l'ancien, `RequestOpen()` du nouveau) n'est en revanche garanti nulle part — un chevauchement
	/// transitoire côté host n'est pas prouvé impossible, mais sans conséquence observable : les deux
	/// RPC sont idempotentes, l'état final converge de toute façon, et l'UI locale se pilote sur
	/// <see cref="LocalOpenContainer"/>/les événements ci-dessus, déjà déterministes.
	/// </summary>
	public void RequestLocalOpen()
	{
		if ( LocalOpenContainer.IsValid() && LocalOpenContainer != this )
			LocalOpenContainer.CloseLocalSessionImmediately();

		_localRequestedContainer = this;
		RequestOpen();
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

	// --- Transferts whole-item ---

	/// <summary>
	/// Point d'entrée réseau unique du transfert conteneur -> joueur ("Take"). <c>[Rpc.Host]</c>
	/// résout <see cref="Rpc.Caller"/> — le conteneur cible est implicite (ce <c>GameObject</c>),
	/// même patron que <see cref="RequestOpen"/>/<see cref="RequestClose"/>/<see cref="RequestSnapshot"/>.
	/// Ne fait confiance à aucune autre donnée du client que l'<c>InstanceId</c> choisi dans son
	/// propre snapshot local (<see cref="LocalEntries"/>) — jamais une définition, une quantité, une
	/// position. Le résultat ciblé (<see cref="SendTransferResultTo"/>) est envoyé exactement une
	/// fois, que la transaction réussisse ou échoue — voir <see cref="TryTakeItemAuthoritative"/>
	/// pour le détail des logs de succès/échec.
	/// </summary>
	[Rpc.Host]
	public void RequestTakeItem( string instanceId )
	{
		var caller = Rpc.Caller;
		var result = TryTakeItemAuthoritative( caller, instanceId );

		if ( !result.Success )
			Log.Warning( $"[WorldContainerTransfer][Take][Fail] '{caller?.DisplayName}' -> '{GameObject.Name}' : instance={instanceId}, reason={result.FailureReason}." );

		if ( caller is null )
		{
			Log.Warning( $"[WorldContainerTransfer][Result][Send] Appelant nul, impossible de cibler le résultat de Take sur '{GameObject.Name}' — log host uniquement." );
			return;
		}

		SendTransferResultTo( caller, result );
	}

	/// <summary>
	/// Transaction métier complète du "Take", indépendante de tout transport réseau — même esprit
	/// que <see cref="TryOpenAuthoritative"/>/<see cref="Kodoku.Items.Interaction.WorldItemPickupComponent.TryPickupAuthoritative"/>.
	/// Ordre de validation strict, voir docs/research/WORLD_CONTAINER_TRANSFER_TESTS.md : autorité
	/// host, appelant/pawn/<c>PlayerController</c>, <see cref="PlayerInventoryComponent"/> et son
	/// <see cref="PlayerInventoryComponent.Container"/>, <see cref="Container"/> de ce conteneur,
	/// appartenance aux <see cref="_viewers"/>, distance (<see cref="IsWithinRange"/> — un échec ici
	/// invalide et retire le viewer, même patron que <see cref="TryResyncAuthoritative"/>) — puis
	/// seulement alors l'<c>InstanceId</c> est parsé : un non-viewer transmettant également un
	/// identifiant invalide reçoit donc <see cref="WorldContainerTransferFailureReason.NotViewer"/>,
	/// jamais <see cref="WorldContainerTransferFailureReason.InvalidInstanceId"/>. La mutation
	/// elle-même (retrait du conteneur monde, ajout dans l'inventaire joueur) passe entièrement par
	/// <see cref="TryTransferItem"/>, partagée avec <see cref="TryStoreItemAuthoritative"/>. Ne
	/// notifie (<see cref="NotifyContentMutated"/> puis <see cref="PlayerInventoryComponent.NotifyMutated"/>,
	/// dans cet ordre précis) qu'après un succès complet — jamais sur un chemin d'échec, quel qu'il
	/// soit.
	/// </summary>
	public WorldContainerTransferResult TryTakeItemAuthoritative( Connection requester, string instanceId )
	{
		const WorldContainerTransferDirection direction = WorldContainerTransferDirection.Take;

		if ( !Networking.IsHost )
		{
			Log.Error( $"[WorldContainerTransfer][InternalError] TryTakeItemAuthoritative() exécuté hors du host pour '{GameObject.Name}'." );
			return WorldContainerTransferResult.Fail( direction, instanceId, WorldContainerTransferFailureReason.InternalError );
		}

		if ( requester is null )
			return WorldContainerTransferResult.Fail( direction, instanceId, WorldContainerTransferFailureReason.InvalidCaller );

		var pawn = KodokuPlayerComponent.FindByConnection( Scene, requester );
		if ( pawn is null || pawn.PlayerController is null )
			return WorldContainerTransferResult.Fail( direction, instanceId, WorldContainerTransferFailureReason.InvalidCaller );

		var inventory = pawn.Components.Get<PlayerInventoryComponent>();
		if ( inventory is null || inventory.Container is null )
			return WorldContainerTransferResult.Fail( direction, instanceId, WorldContainerTransferFailureReason.InvalidCaller );

		if ( Container is null )
		{
			Log.Error( $"[WorldContainerTransfer][InternalError] Container absent sur '{GameObject.Name}' pour un host pourtant valide." );
			return WorldContainerTransferResult.Fail( direction, instanceId, WorldContainerTransferFailureReason.InternalError );
		}

		if ( !_viewers.Contains( requester ) )
			return WorldContainerTransferResult.Fail( direction, instanceId, WorldContainerTransferFailureReason.NotViewer );

		if ( !IsWithinRange( pawn ) )
		{
			SendInvalidationTo( requester, "out of range" );
			_viewers.Remove( requester );
			Log.Info( $"[WorldContainer][ViewerRemoved] '{requester?.DisplayName}' -> '{GameObject.Name}' (out of range on transfer). Total={_viewers.Count}." );
			return WorldContainerTransferResult.Fail( direction, instanceId, WorldContainerTransferFailureReason.OutOfRange );
		}

		if ( !Guid.TryParse( instanceId, out var parsedId ) || parsedId == Guid.Empty )
			return WorldContainerTransferResult.Fail( direction, instanceId, WorldContainerTransferFailureReason.InvalidInstanceId );

		int worldRevisionBefore = HostRevision;
		int playerRevisionBefore = inventory.HostRevision;

		var failure = TryTransferItem( Container, inventory.Container, parsedId );
		if ( failure != WorldContainerTransferFailureReason.None )
			return WorldContainerTransferResult.Fail( direction, instanceId, failure );

		// Succès complet confirmé — notifier dans l'ordre source (conteneur) puis destination
		// (joueur), jamais l'inverse pour ce sens (voir docs/research/WORLD_CONTAINER_TRANSFER_TESTS.md).
		NotifyContentMutated();
		inventory.NotifyMutated();

		Log.Info( $"[WorldContainerTransfer][Take][Success] '{requester?.DisplayName}' : instance={parsedId}, " +
			$"conteneurRevision {worldRevisionBefore}->{HostRevision}, joueurRevision {playerRevisionBefore}->{inventory.HostRevision}." );

		return WorldContainerTransferResult.Ok( direction, instanceId );
	}

	/// <summary>
	/// Point d'entrée réseau unique du transfert joueur -> conteneur ("Store"). Même patron que
	/// <see cref="RequestTakeItem"/>, direction inversée.
	/// </summary>
	[Rpc.Host]
	public void RequestStoreItem( string instanceId )
	{
		var caller = Rpc.Caller;
		var result = TryStoreItemAuthoritative( caller, instanceId );

		if ( !result.Success )
			Log.Warning( $"[WorldContainerTransfer][Store][Fail] '{caller?.DisplayName}' -> '{GameObject.Name}' : instance={instanceId}, reason={result.FailureReason}." );

		if ( caller is null )
		{
			Log.Warning( $"[WorldContainerTransfer][Result][Send] Appelant nul, impossible de cibler le résultat de Store sur '{GameObject.Name}' — log host uniquement." );
			return;
		}

		SendTransferResultTo( caller, result );
	}

	/// <summary>
	/// Transaction métier complète du "Store" — même ordre de validation exact que
	/// <see cref="TryTakeItemAuthoritative"/>, source et destination inversées. Notifie dans l'ordre
	/// inverse de Take : source (joueur, <see cref="PlayerInventoryComponent.NotifyMutated"/>) puis
	/// destination (conteneur, <see cref="NotifyContentMutated"/>).
	/// </summary>
	public WorldContainerTransferResult TryStoreItemAuthoritative( Connection requester, string instanceId )
	{
		const WorldContainerTransferDirection direction = WorldContainerTransferDirection.Store;

		if ( !Networking.IsHost )
		{
			Log.Error( $"[WorldContainerTransfer][InternalError] TryStoreItemAuthoritative() exécuté hors du host pour '{GameObject.Name}'." );
			return WorldContainerTransferResult.Fail( direction, instanceId, WorldContainerTransferFailureReason.InternalError );
		}

		if ( requester is null )
			return WorldContainerTransferResult.Fail( direction, instanceId, WorldContainerTransferFailureReason.InvalidCaller );

		var pawn = KodokuPlayerComponent.FindByConnection( Scene, requester );
		if ( pawn is null || pawn.PlayerController is null )
			return WorldContainerTransferResult.Fail( direction, instanceId, WorldContainerTransferFailureReason.InvalidCaller );

		var inventory = pawn.Components.Get<PlayerInventoryComponent>();
		if ( inventory is null || inventory.Container is null )
			return WorldContainerTransferResult.Fail( direction, instanceId, WorldContainerTransferFailureReason.InvalidCaller );

		if ( Container is null )
		{
			Log.Error( $"[WorldContainerTransfer][InternalError] Container absent sur '{GameObject.Name}' pour un host pourtant valide." );
			return WorldContainerTransferResult.Fail( direction, instanceId, WorldContainerTransferFailureReason.InternalError );
		}

		if ( !_viewers.Contains( requester ) )
			return WorldContainerTransferResult.Fail( direction, instanceId, WorldContainerTransferFailureReason.NotViewer );

		if ( !IsWithinRange( pawn ) )
		{
			SendInvalidationTo( requester, "out of range" );
			_viewers.Remove( requester );
			Log.Info( $"[WorldContainer][ViewerRemoved] '{requester?.DisplayName}' -> '{GameObject.Name}' (out of range on transfer). Total={_viewers.Count}." );
			return WorldContainerTransferResult.Fail( direction, instanceId, WorldContainerTransferFailureReason.OutOfRange );
		}

		if ( !Guid.TryParse( instanceId, out var parsedId ) || parsedId == Guid.Empty )
			return WorldContainerTransferResult.Fail( direction, instanceId, WorldContainerTransferFailureReason.InvalidInstanceId );

		int playerRevisionBefore = inventory.HostRevision;
		int worldRevisionBefore = HostRevision;

		var failure = TryTransferItem( inventory.Container, Container, parsedId );
		if ( failure != WorldContainerTransferFailureReason.None )
			return WorldContainerTransferResult.Fail( direction, instanceId, failure );

		// Succès complet confirmé — notifier dans l'ordre source (joueur) puis destination
		// (conteneur), symétrique de Take.
		inventory.NotifyMutated();
		NotifyContentMutated();

		Log.Info( $"[WorldContainerTransfer][Store][Success] '{requester?.DisplayName}' : instance={parsedId}, " +
			$"joueurRevision {playerRevisionBefore}->{inventory.HostRevision}, conteneurRevision {worldRevisionBefore}->{HostRevision}." );

		return WorldContainerTransferResult.Ok( direction, instanceId );
	}

	/// <summary>
	/// Algorithme partagé des deux sens de transfert — même code exact pour Take et Store, seuls
	/// <paramref name="source"/>/<paramref name="destination"/> sont inversés par l'appelant. Ordre :
	/// retrouver le placement source, préflight pur de la destination
	/// (<see cref="InventoryContainer.TryFindFirstFit"/>, aucune mutation en cas d'échec), puis
	/// seulement alors <see cref="InventoryContainer.TryRemove"/> sur la source suivi de
	/// <see cref="InventoryContainer.TryAdd"/> sur la destination à la position exacte planifiée par
	/// le préflight — jamais un second scan first-fit après le retrait. Ne notifie jamais, ne
	/// journalise jamais le succès : ces deux responsabilités restent dans
	/// <see cref="TryTakeItemAuthoritative"/>/<see cref="TryStoreItemAuthoritative"/>, qui
	/// connaissent le contexte complet (appelant, direction, révisions avant/après) nécessaire aux
	/// logs de production.
	/// </summary>
	WorldContainerTransferFailureReason TryTransferItem( InventoryContainer source, InventoryContainer destination, Guid instanceId )
	{
		var placement = source.GetPlacement( instanceId );
		if ( placement is null )
			return WorldContainerTransferFailureReason.ItemNotFound;

		var item = placement.Item;
		int originalX = placement.X;
		int originalY = placement.Y;
		bool originalRotated = placement.IsRotated;

		// Préflight pur : aucune mutation tant que la destination n'a pas explicitement confirmé
		// pouvoir accepter cet item — voir InventoryContainer.TryFindFirstFit.
		var preflight = destination.TryFindFirstFit( item );
		if ( !preflight.Success )
		{
			// NoAvailableSpace -> DestinationNoSpace (cas normal, grille pleine ou item trop grand).
			// AlreadyContained ou toute autre raison -> InternalError : une ItemInstance ne vit
			// légitimement que dans un seul InventoryContainer à la fois, atteindre ce cas
			// signifierait un état canonique déjà incohérent avant même ce transfert.
			return preflight.FailureReason == InventoryFailureReason.NoAvailableSpace
				? WorldContainerTransferFailureReason.DestinationNoSpace
				: WorldContainerTransferFailureReason.InternalError;
		}

		var candidate = preflight.Placement;

		// Retrait de la source — seulement maintenant que la destination a confirmé pouvoir
		// accepter. Même référence ItemInstance, même InstanceId, garantis par construction
		// (TryRemove ne retourne que le placement déjà résolu par instanceId ci-dessus).
		var removeResult = source.TryRemove( instanceId, out var removedItem );
		if ( !removeResult.Success || removedItem != item )
		{
			Log.Error( $"[WorldContainerTransfer][InternalError] TryRemove a échoué après validation complète pour instance={instanceId} — état canonique potentiellement incohérent." );
			return WorldContainerTransferFailureReason.InternalError;
		}

		// Ajout planifié : même item, même position que celle validée par le préflight, aucun
		// second scan first-fit. Devrait toujours réussir — rien ne mute la destination entre le
		// préflight et cet appel, dans le même traitement host synchrone (voir
		// InventoryContainer.TryFindFirstFit et docs/research/WORLD_CONTAINER_TRANSFER_TESTS.md).
		var addResult = destination.TryAdd( item, candidate.X, candidate.Y, candidate.IsRotated );
		if ( addResult.Success )
			return WorldContainerTransferFailureReason.None;

		// Anomalie : le préflight avait pourtant validé ce candidat. Filet de sécurité, jamais le
		// chemin normal — ne pas provoquer artificiellement ce cas en production.
		Log.Error( $"[WorldContainerTransfer][UnexpectedPlannedAddFailure] instance={instanceId} : l'ajout planifié à " +
			$"({candidate.X},{candidate.Y}) rotated={candidate.IsRotated} a échoué malgré un préflight réussi ({addResult.FailureReason})." );

		var rollback = source.TryAdd( item, originalX, originalY, originalRotated );
		if ( !rollback.Success )
		{
			Log.Error( $"[WorldContainerTransfer][CriticalRollbackFailure] instance={instanceId} n'a pas pu être réinsérée à " +
				$"({originalX},{originalY}) rotated={originalRotated} après échec de l'ajout planifié ({addResult.FailureReason}) : " +
				$"{rollback.FailureReason}. Item perdu de l'état canonique — aucune nouvelle instance n'est créée pour compenser, " +
				$"une intervention manuelle peut être nécessaire." );
			return WorldContainerTransferFailureReason.RollbackFailed;
		}

		return WorldContainerTransferFailureReason.InternalError;
	}

	/// <summary>
	/// Envoie le résultat ciblé d'un transfert au seul appelant à l'origine de la requête — jamais
	/// aux autres viewers, jamais via <c>[Rpc.Owner]</c> (ce conteneur n'a pas de <c>Network Owner</c>
	/// joueur, ADR-0006) : même transport que <see cref="SendInvalidationTo"/> (<c>[Rpc.Broadcast]</c>
	/// + <see cref="Rpc.FilterInclude(Sandbox.Connection)"/>). Accusé de traitement, pas un second
	/// canal d'état canonique — voir <see cref="WorldContainerTransferResult"/>.
	/// </summary>
	void SendTransferResultTo( Connection target, WorldContainerTransferResult result )
	{
		Log.Info( $"[WorldContainerTransfer][Result][Send] '{GameObject.Name}' -> '{target?.DisplayName}' : " +
			$"success={result.Success}, direction={result.Direction}, instance={result.InstanceId}, reason={result.FailureReason}." );

		using ( Rpc.FilterInclude( target ) )
		{
			ReceiveTransferResult( result.Success, result.Direction, result.InstanceId, result.FailureReason );
		}
	}

	/// <summary>
	/// Reçoit le résultat ciblé d'un transfert — exécutée uniquement chez le viewer ciblé par le
	/// filtre. Alimente uniquement <see cref="LastTransferResult"/> (présentation/tests) — jamais
	/// une preuve que les snapshots de contenu ont déjà été appliqués localement (voir
	/// <see cref="LocalRevision"/>/<see cref="PlayerInventoryComponent.LocalRevision"/> pour cela).
	/// </summary>
	[Rpc.Broadcast]
	public void ReceiveTransferResult( bool success, WorldContainerTransferDirection direction, string instanceId, WorldContainerTransferFailureReason failureReason )
	{
		LastTransferResult = success
			? WorldContainerTransferResult.Ok( direction, instanceId )
			: WorldContainerTransferResult.Fail( direction, instanceId, failureReason );
		HasLocalTransferResult = true;
		LocalTransferResultSequence++;

		Log.Info( $"[WorldContainerTransfer][Result][Receive] '{GameObject.Name}' : success={success}, direction={direction}, " +
			$"instance={instanceId}, reason={failureReason}, sequence={LocalTransferResultSequence}." );
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

		bool wasOpen = IsLocalSessionOpen;

		// Filtre anti-réponse-retardée (voir _localRequestedContainer) : une toute nouvelle session
		// (jamais ouverte localement pour CE conteneur) ne devient réelle que si ce conteneur est
		// encore celui explicitement demandé par le joueur en dernier. Sinon, c'est la confirmation
		// tardive d'un choix déjà abandonné (deux ouvertures rapprochées sur des conteneurs
		// différents, réponses reçues dans un ordre non garanti) — aucun état local n'est modifié,
		// le host est simplement notifié de fermer cette session qui ne sera jamais affichée.
		if ( !wasOpen && _localRequestedContainer != this )
		{
			Log.Info( $"[WorldContainer][Snapshot][Superseded] '{GameObject.Name}' : snapshot d'ouverture reçu, mais plus le conteneur demandé localement — fermeture immédiate, aucun état local modifié." );
			RequestClose();
			return;
		}

		LocalRevision = revision;
		LocalWidth = width;
		LocalHeight = height;
		LocalEntries = entries ?? Array.Empty<WorldContainerSnapshotEntry>();
		IsLocalSessionOpen = true;
		LocalInvalidationReason = "";

		Log.Info( $"[WorldContainer][Snapshot][Receive] '{GameObject.Name}' : revision={LocalRevision}, entries={LocalEntries.Count}, dims={LocalWidth}x{LocalHeight}." );

		// Transition fermé -> ouvert uniquement : une resynchronisation (déjà ouvert) ne doit pas
		// re-déclencher l'événement d'ouverture pour une future UI qui y réagirait en l'ouvrant à
		// nouveau. Voir LocalOpenContainer.
		if ( !wasOpen )
		{
			LocalOpenContainer = this;
			LocalContainerOpened?.Invoke( this );

			// Intention consommée : _localRequestedContainer ne représente qu'une demande EN
			// ATTENTE (voir sa doc), jamais une session ouverte — la nettoyer ici sépare clairement
			// les deux états et évite qu'une valeur périmée ("A" toujours marqué comme demandé
			// après avoir été accepté puis fermé par un tout autre chemin) ne fasse
			// accidentellement passer un futur snapshot tardif de A comme "encore demandé".
			ClearLocalRequestedContainerIfSelf();
		}
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
		ApplyLocalInvalidate( reason ?? "" );
	}

	/// <summary>
	/// Effet local de fermeture, factorisé entre <see cref="ReceiveInvalidate"/> (déclenché par le
	/// host, réseau) et <see cref="CloseLocalSessionImmediately"/> (déclenché localement, sans
	/// attendre le réseau) — les deux chemins doivent vider le cache local et notifier
	/// <see cref="LocalContainerClosed"/> exactement de la même façon.
	/// </summary>
	void ApplyLocalInvalidate( string reason )
	{
		LocalEntries = Array.Empty<WorldContainerSnapshotEntry>();
		LocalRevision = -1;
		LocalWidth = 0;
		LocalHeight = 0;
		IsLocalSessionOpen = false;
		LocalInvalidationReason = reason;

		Log.Info( $"[WorldContainer][Invalidate] '{GameObject.Name}' : session invalidée ({LocalInvalidationReason}), cache local vidé." );

		ClearLocalOpenContainerIfSelf();

		// Filet de sécurité : une intention est normalement déjà consommée par ReceiveSnapshot avant
		// toute invalidation normale ; nettoyée ici aussi pour rester correcte si ce conteneur est
		// invalidé/fermé avant d'avoir jamais été confirmé.
		ClearLocalRequestedContainerIfSelf();
	}

	/// <summary>
	/// Ferme la session locale de CE conteneur immédiatement, sans attendre la confirmation réseau du
	/// host — même rationale que la fermeture volontaire déjà documentée (section 14 de
	/// WORLD_CONTAINER_ARCHITECTURE.md : « le client n'a pas besoin d'attendre une confirmation
	/// réseau pour son propre affichage »). Utilisée uniquement quand le joueur local abandonne CE
	/// conteneur au profit d'un autre (voir <see cref="RequestLocalOpen"/>) : garantit que
	/// <see cref="LocalContainerClosed"/> est émis de façon déterministe AVANT que le nouveau
	/// conteneur ne devienne actif, quel que soit l'ordre d'arrivée réseau des RPC. Notifie quand
	/// même le host (<see cref="RequestClose"/>, idempotent) pour un retrait réel de la liste de
	/// viewers — sans cet appel, ce conteneur resterait viewer côté host indéfiniment alors qu'aucun
	/// client local ne l'affiche plus (fuite de session, pas seulement un affichage obsolète).
	/// No-op si aucune session locale n'est actuellement ouverte pour ce conteneur.
	/// </summary>
	public void CloseLocalSessionImmediately()
	{
		if ( !IsLocalSessionOpen )
			return;

		ApplyLocalInvalidate( "superseded" );
		RequestClose();
	}

	/// <summary>
	/// Nettoyage local si ce conteneur disparaît (déconnexion, destruction de scène) alors qu'il
	/// était <see cref="LocalOpenContainer"/> et/ou <see cref="_localRequestedContainer"/> — évite
	/// qu'une future UI garde une référence vers un composant détruit, et qu'une intention obsolète
	/// pointe vers un composant qui ne recevra plus jamais de snapshot. Même garde que
	/// <see cref="Kodoku.Player.KodokuPlayerComponent.OnDestroy"/>.
	/// </summary>
	protected override void OnDestroy()
	{
		ClearLocalRequestedContainerIfSelf();
		ClearLocalOpenContainerIfSelf();
	}

	void ClearLocalOpenContainerIfSelf()
	{
		if ( LocalOpenContainer != this )
			return;

		LocalOpenContainer = null;
		LocalContainerClosed?.Invoke( this );
	}

	/// <summary>
	/// Nettoie <see cref="_localRequestedContainer"/> uniquement s'il référence CE conteneur — jamais un
	/// autre, pour ne jamais effacer l'intention d'un autre conteneur que le joueur vient de demander.
	/// </summary>
	void ClearLocalRequestedContainerIfSelf()
	{
		if ( _localRequestedContainer == this )
			_localRequestedContainer = null;
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
