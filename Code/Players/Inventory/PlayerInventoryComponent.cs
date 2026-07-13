using System;
using System.Linq;
using Kodoku.Items.Inventory;

namespace Kodoku.Player.Inventory;

/// <summary>
/// Inventaire réseau d'un pawn Kodoku — porte un <see cref="InventoryContainer"/> host-authoritative
/// (cohérent avec ADR-0002, même choix que <see cref="Vitals.PlayerVitalsComponent"/>), <see cref="Container"/>
/// n'existant que côté host (voir <see cref="OnStart"/>). Le contenu est répliqué en lecture seule vers le
/// seul propriétaire du pawn via un snapshot complet (<see cref="LocalEntries"/>/<see cref="LocalRevision"/>),
/// jamais par delta — voir <see cref="NotifyMutated"/>/<see cref="ReceiveSnapshot"/>. Aucun autre client ne
/// reçoit ce contenu. <see cref="Container"/> (canonique, host) et <see cref="LocalEntries"/> (cache de
/// présentation, propriétaire) sont deux états distincts à ne jamais confondre — seul <see cref="Container"/>
/// fait foi pour une future mutation (drop, déplacement...), jamais <see cref="LocalEntries"/>.
/// Voir docs/architecture/ITEM_ARCHITECTURE.md.
/// </summary>
public sealed class PlayerInventoryComponent : Component, IGameObjectNetworkEvents
{
	[Group( "Configuration" )]
	[Property]
	public int Width { get; set; } = 6;

	[Group( "Configuration" )]
	[Property]
	public int Height { get; set; } = 6;

	/// <summary>
	/// Null sur toute instance qui n'est pas celle du host (voir commentaire de classe). Un appelant
	/// doit toujours vérifier <see cref="Sandbox.Networking.IsHost"/> avant de muter ce conteneur —
	/// jamais exposé pour une mutation directe depuis un client.
	/// </summary>
	public InventoryContainer Container { get; private set; }

	/// <summary>
	/// Nombre d'objets actuellement placés — lecture seule, pour inspection en éditeur pendant
	/// l'exécution. Vaut toujours 0 sur une instance non-host (voir <see cref="Container"/>) : ne
	/// reflète le contenu réel que dans l'inspecteur du host, jamais celui d'un client distant.
	/// </summary>
	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public int Count => Container?.Count ?? 0;

	/// <summary>Poids total actuellement porté — mêmes réserves que <see cref="Count"/>.</summary>
	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public float CurrentWeight => Container?.CurrentWeight ?? 0f;

	/// <summary>
	/// Résumé lisible du contenu (ex. « Water Bottle x2 »), pour voir en un coup d'œil ce que porte
	/// ce pawn depuis l'inspecteur du host, sans UI dédiée. Mêmes réserves que <see cref="Count"/>.
	/// </summary>
	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public string Contents => Container is null
		? ""
		: string.Join( ", ", Container.Placements.Select( p => $"{p.Item.Definition.DisplayName} x{p.Item.Quantity}" ) );

	/// <summary>
	/// Révision host-authoritative du <see cref="Container"/> canonique — incrémentée uniquement par
	/// <see cref="NotifyMutated"/>, jamais directement ailleurs. Vaut toujours 0 sur une instance
	/// non-host (jamais lue là-bas — voir <see cref="LocalRevision"/> pour la révision côté cache).
	/// </summary>
	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public int HostRevision { get; private set; }

	/// <summary>
	/// Cache local en lecture seule destiné à la présentation (UI debug, future UI finale) —
	/// remplacé entièrement à chaque snapshot accepté (<see cref="ReceiveSnapshot"/>), jamais muté
	/// autrement. Vide tant qu'aucun snapshot n'a été reçu, ou pour un pawn proxy
	/// (voir <see cref="Sandbox.IGameObjectNetworkEvents.StopControl"/>). Ne jamais utiliser cette
	/// collection comme preuve qu'une opération est autorisée — seul <see cref="Container"/>, côté
	/// host, fait foi.
	/// </summary>
	public IReadOnlyList<InventorySnapshotEntry> LocalEntries { get; private set; } = Array.Empty<InventorySnapshotEntry>();

	/// <summary>Révision du dernier snapshot accepté par <see cref="ReceiveSnapshot"/>. -1 tant qu'aucun n'a été reçu.</summary>
	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public int LocalRevision { get; private set; } = -1;

	/// <summary>Nombre d'entrées dans <see cref="LocalEntries"/> — pour inspection rapide, même esprit que <see cref="Count"/>.</summary>
	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public int LocalItemCount => LocalEntries.Count;

	protected override void OnStart()
	{
		// Même choix que WorldItemComponent/LootSpawnPointComponent : Networking.IsHost, pas IsProxy
		// (timing de IsProxy juste après spawn documenté comme risque — voir MULTIPLAYER_ARCHITECTURE.md).
		if ( Networking.IsHost )
			Container = new InventoryContainer( Width, Height );

		// Couvre le cas confirmé par KodokuPlayerComponent (voir son commentaire OnStart) : le pawn du
		// host est contrôlé par lui dès sa création, sans transition détectable par StartControl seul.
		if ( !IsProxy )
			RequestSnapshot();
	}

	// IGameObjectNetworkEvents — déclenché uniquement sur l'instance dont l'état de contrôle change,
	// jamais reçu par un proxy distant. Couvre une prise de contrôle après le premier frame (late join,
	// transfert d'ownership) ; voir OnStart pour le cas du pawn contrôlé dès sa création.
	void IGameObjectNetworkEvents.StartControl() => RequestSnapshot();

	// Perte de contrôle (proxy redevenu, ex. transfert d'ownership) : vide le cache de présentation
	// sans jamais toucher à Container (de toute façon toujours null sur une instance non-host).
	void IGameObjectNetworkEvents.StopControl()
	{
		LocalEntries = Array.Empty<InventorySnapshotEntry>();
		LocalRevision = -1;
		Log.Info( $"[InventorySync][LocalCacheCleared] '{GameObject.Name}' a perdu le contrôle — cache local vidé." );
	}

	/// <summary>
	/// Point d'entrée central pour toute mutation validée de <see cref="Container"/> — appelé par
	/// <see cref="Kodoku.Items.Interaction.WorldItemPickupComponent"/> après un pickup réussi, et par
	/// tout futur système de mutation (drop, déplacement...). Host-only : incrémente
	/// <see cref="HostRevision"/> puis reconstruit et envoie un snapshot complet au seul propriétaire.
	/// Ne fait jamais de mutation elle-même — appelée après que la mutation de <see cref="Container"/>
	/// a déjà réussi.
	/// </summary>
	public void NotifyMutated()
	{
		if ( !Networking.IsHost )
		{
			Log.Error( $"[InventorySync][InternalError] NotifyMutated() appelé hors du host pour '{GameObject.Name}'." );
			return;
		}

		HostRevision++;
		SendFullSnapshotToOwner();
	}

	/// <summary>
	/// Reconstruit un snapshot complet depuis <see cref="Container"/> et l'envoie au seul propriétaire
	/// de ce pawn (<c>[Rpc.Owner]</c>, voir <see cref="ReceiveSnapshot"/>) — jamais aux autres clients.
	/// Host-only. N'incrémente jamais <see cref="HostRevision"/> elle-même (voir <see cref="NotifyMutated"/>
	/// pour une mutation réelle) : appelée telle quelle pour un envoi initial ou une resynchronisation
	/// explicite, où la révision courante doit être renvoyée inchangée.
	/// </summary>
	void SendFullSnapshotToOwner()
	{
		if ( !Networking.IsHost || Container is null )
			return;

		var entries = Container.Placements
			.Select( p => new InventorySnapshotEntry( p.InstanceId.ToString(), p.Item.Definition.ItemId, p.Item.Quantity, p.X, p.Y, p.IsRotated ) )
			.ToArray();

		Log.Info( $"[InventorySync][Send] '{GameObject.Name}' -> {GameObject.Network.Owner?.DisplayName ?? "(host, sans owner explicite)"} : " +
			$"revision={HostRevision}, entries={entries.Length}." );

		ReceiveSnapshot( HostRevision, entries );
	}

	/// <summary>
	/// Reçoit un snapshot complet depuis le host — exécutée uniquement chez le propriétaire de ce pawn
	/// (<c>[Rpc.Owner]</c>, ou chez le host si le GameObject n'a pas d'owner résolu ; ce repli ne se
	/// déclenche jamais en pratique pour <c>kodoku_player</c>, toujours possédé dès son spawn). Remplace
	/// entièrement <see cref="LocalEntries"/> — jamais de fusion/patch partiel (snapshot complet, pas de
	/// delta, décision volontaire pour cette V1 — voir docs/architecture/ITEM_ARCHITECTURE.md). Un
	/// snapshot de révision strictement inférieure à <see cref="LocalRevision"/> est ignoré ; une révision
	/// égale est acceptée (nécessaire pour qu'une resynchronisation explicite reconstruise le cache même
	/// sans nouvelle mutation côté host — voir <see cref="RequestSnapshot"/>).
	/// </summary>
	[Rpc.Owner]
	public void ReceiveSnapshot( int revision, InventorySnapshotEntry[] entries )
	{
		if ( revision < LocalRevision )
		{
			Log.Warning( $"[InventorySync][IgnoredOldRevision] '{GameObject.Name}' : reçu revision={revision}, " +
				$"déjà à revision={LocalRevision} — ignoré." );
			return;
		}

		LocalRevision = revision;
		LocalEntries = entries ?? Array.Empty<InventorySnapshotEntry>();

		Log.Info( $"[InventorySync][Receive] '{GameObject.Name}' : revision={LocalRevision}, entries={LocalEntries.Count}." );
	}

	/// <summary>
	/// Demande un snapshot complet au host — sert à la fois de prise de contrôle initiale
	/// (<see cref="OnStart"/>/<see cref="Sandbox.IGameObjectNetworkEvents.StartControl"/>) et de
	/// resynchronisation explicite (ex. outil debug). <c>[Rpc.Host]</c> : le host résout
	/// <see cref="Rpc.Caller"/> et vérifie explicitement qu'il correspond bien au propriétaire réseau
	/// de CE pawn avant d'envoyer quoi que ce soit — un client ne peut donc jamais obtenir le snapshot
	/// d'un autre joueur en appelant cette méthode sur un pawn qui n'est pas le sien (tous les pawns
	/// sont networkés et donc visibles/appelables par tout client, pas seulement le sien). Jamais de
	/// mutation de <see cref="Container"/> ici — une resync ne fait que renvoyer l'état déjà existant.
	/// </summary>
	[Rpc.Host]
	public void RequestSnapshot()
	{
		var caller = Rpc.Caller;

		if ( GameObject.Network.Owner != caller )
		{
			Log.Warning( $"[InventorySync][OwnershipRejected] '{caller?.DisplayName}' a demandé le snapshot de " +
				$"'{GameObject.Name}', dont il n'est pas le propriétaire — refusé." );
			return;
		}

		Log.Info( $"[InventorySync][ResyncRequest] '{caller?.DisplayName}' -> '{GameObject.Name}'." );
		SendFullSnapshotToOwner();
	}

	/// <summary>
	/// Outil de debug uniquement (voir <see cref="Kodoku.UI.InventoryDebugPanel"/>, Test 6 de la mission
	/// snapshot d'inventaire) : altère volontairement le cache local pour vérifier qu'une resynchronisation
	/// explicite (<see cref="RequestSnapshot"/>) le reconstruit fidèlement depuis <see cref="Container"/>.
	/// Ne touche jamais <see cref="Container"/> — ce n'est pas une mutation de gameplay, uniquement un
	/// moyen de corrompre l'état de présentation pour tester le mécanisme de resync lui-même.
	/// </summary>
	public void DebugCorruptLocalCache()
	{
		LocalEntries = Array.Empty<InventorySnapshotEntry>();
		LocalRevision = -1;
		Log.Info( $"[InventorySync][DebugCorruptLocalCache] '{GameObject.Name}' : cache local vidé manuellement (debug)." );
	}
}
