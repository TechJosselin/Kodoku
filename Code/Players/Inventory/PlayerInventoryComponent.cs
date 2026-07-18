using System;
using System.Linq;
using Kodoku.Items;
using Kodoku.Items.Inventory;
using Kodoku.Player;

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
///
/// Depuis le jalon « Équipement corporel minimal » (voir docs/architecture/ITEM_ARCHITECTURE.md), ce
/// composant porte aussi l'état canonique d'équipement (<see cref="_equippedSlots"/>) — même choix
/// d'intégration que <see cref="Container"/> (Approche B de la conception : équipement et grille sont
/// deux emplacements du même concept, « où vit cette <see cref="ItemInstance"/> côté host pour ce
/// pawn », jamais deux domaines séparés). Le snapshot propriétaire est désormais combiné : grille et
/// équipement voyagent ensemble sous une seule <see cref="HostRevision"/>/<see cref="LocalRevision"/>,
/// jamais deux révisions séparées.
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
	/// État canonique d'équipement — host-only, jamais peuplé sur une instance non-host (même
	/// garantie que <see cref="Container"/>, qui reste seulement <c>null</c> là-bas plutôt que vide,
	/// mais la conséquence pratique est identique : aucune mutation de client possible). Clé =
	/// <see cref="EquipmentSlotType.Head"/>/<see cref="EquipmentSlotType.Body"/> uniquement — jamais
	/// <see cref="EquipmentSlotType.None"/>, jamais absent puis présent avec une valeur nulle
	/// (un slot vide est simplement absent de ce dictionnaire, pas une entrée avec une valeur
	/// <c>null</c>). N'expose aucune collection mutable publiquement — voir
	/// <see cref="EquippedCount"/>/<see cref="EquippedContents"/> pour la lecture (host uniquement,
	/// même esprit que <see cref="Count"/>/<see cref="Contents"/>) et <see cref="LocalEquipment"/>
	/// pour le cache répliqué côté propriétaire.
	/// </summary>
	readonly Dictionary<EquipmentSlotType, ItemInstance> _equippedSlots = new();

	/// <summary>Nombre de slots actuellement occupés — mêmes réserves que <see cref="Count"/> (host uniquement).</summary>
	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public int EquippedCount => _equippedSlots.Count;

	/// <summary>Résumé lisible de l'équipement (ex. « Head: Test Helmet »), mêmes réserves que <see cref="Contents"/>.</summary>
	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public string EquippedContents => _equippedSlots.Count == 0
		? ""
		: string.Join( ", ", _equippedSlots.Select( kv => $"{kv.Key}: {kv.Value.Definition.DisplayName}" ) );

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

	/// <summary>
	/// Cache local en lecture seule de l'équipement — mêmes garanties que <see cref="LocalEntries"/> :
	/// remplacé entièrement à chaque snapshot combiné accepté, jamais muté autrement, jamais une
	/// preuve d'autorisation (seul <see cref="_equippedSlots"/>, côté host, fait foi). Appliqué en
	/// même temps que <see cref="LocalEntries"/> et sous la même <see cref="LocalRevision"/> — jamais
	/// une révision intermédiaire où l'un serait à jour et l'autre non (voir <see cref="ReceiveSnapshot"/>).
	/// </summary>
	public IReadOnlyList<EquipmentSnapshotEntry> LocalEquipment { get; private set; } = Array.Empty<EquipmentSnapshotEntry>();

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

	/// <summary>Nombre d'entrées dans <see cref="LocalEquipment"/> — même esprit que <see cref="LocalItemCount"/>.</summary>
	[Group( "Runtime" )]
	[ReadOnly]
	[Property]
	public int LocalEquippedCount => LocalEquipment.Count;

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
		LocalEquipment = Array.Empty<EquipmentSnapshotEntry>();
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
	/// Reconstruit un snapshot combiné (grille + équipement) depuis l'état canonique et l'envoie au
	/// seul propriétaire de ce pawn (<c>[Rpc.Owner]</c>, voir <see cref="ReceiveSnapshot"/>) — jamais
	/// aux autres clients. Host-only. N'incrémente jamais <see cref="HostRevision"/> elle-même (voir
	/// <see cref="NotifyMutated"/> pour une mutation réelle) : appelée telle quelle pour un envoi
	/// initial ou une resynchronisation explicite, où la révision courante doit être renvoyée
	/// inchangée. Snapshot combiné, pas deux snapshots séparés : grille et équipement voyagent dans
	/// le même appel RPC, sous la même révision — aucune fenêtre où le client aurait une grille à jour
	/// et un équipement obsolète, ou l'inverse (voir docs/architecture/ITEM_ARCHITECTURE.md).
	/// </summary>
	void SendFullSnapshotToOwner()
	{
		if ( !Networking.IsHost || Container is null )
			return;

		var entries = Container.Placements
			.Select( p => new InventorySnapshotEntry( p.InstanceId.ToString(), p.Item.Definition.ItemId, p.Item.Quantity, p.X, p.Y, p.IsRotated ) )
			.ToArray();

		var equipment = _equippedSlots
			.Select( kv => new EquipmentSnapshotEntry( kv.Key, kv.Value.InstanceId.ToString(), kv.Value.Definition.ItemId, kv.Value.Quantity ) )
			.ToArray();

		Log.Info( $"[InventorySync][Send] '{GameObject.Name}' -> {GameObject.Network.Owner?.DisplayName ?? "(host, sans owner explicite)"} : " +
			$"revision={HostRevision}, entries={entries.Length}, equipped={equipment.Length}." );

		ReceiveSnapshot( HostRevision, entries, equipment );
	}

	/// <summary>
	/// Reçoit un snapshot combiné depuis le host — exécutée uniquement chez le propriétaire de ce pawn
	/// (<c>[Rpc.Owner]</c>, ou chez le host si le GameObject n'a pas d'owner résolu ; ce repli ne se
	/// déclenche jamais en pratique pour <c>kodoku_player</c>, toujours possédé dès son spawn). Remplace
	/// entièrement <see cref="LocalEntries"/> **et** <see cref="LocalEquipment"/> ensemble, sous la même
	/// <see cref="LocalRevision"/> — jamais de fusion/patch partiel, jamais l'un mis à jour sans l'autre
	/// (snapshot complet, pas de delta, décision volontaire pour cette V1 — voir
	/// docs/architecture/ITEM_ARCHITECTURE.md). Un snapshot de révision strictement inférieure à
	/// <see cref="LocalRevision"/> est ignoré ; une révision égale est acceptée (nécessaire pour qu'une
	/// resynchronisation explicite reconstruise le cache même sans nouvelle mutation côté host — voir
	/// <see cref="RequestSnapshot"/>).
	/// </summary>
	[Rpc.Owner]
	public void ReceiveSnapshot( int revision, InventorySnapshotEntry[] entries, EquipmentSnapshotEntry[] equipment )
	{
		if ( revision < LocalRevision )
		{
			Log.Warning( $"[InventorySync][IgnoredOldRevision] '{GameObject.Name}' : reçu revision={revision}, " +
				$"déjà à revision={LocalRevision} — ignoré." );
			return;
		}

		LocalRevision = revision;
		LocalEntries = entries ?? Array.Empty<InventorySnapshotEntry>();
		LocalEquipment = equipment ?? Array.Empty<EquipmentSnapshotEntry>();

		Log.Info( $"[InventorySync][Receive] '{GameObject.Name}' : revision={LocalRevision}, entries={LocalEntries.Count}, equipped={LocalEquipment.Count}." );
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
		LocalEquipment = Array.Empty<EquipmentSnapshotEntry>();
		LocalRevision = -1;
		Log.Info( $"[InventorySync][DebugCorruptLocalCache] '{GameObject.Name}' : cache local vidé manuellement (debug)." );
	}

	/// <summary>
	/// Point d'entrée réseau unique de l'équipement. Même schéma que
	/// <see cref="Kodoku.Player.Inventory.PlayerItemDropComponent.RequestDrop"/> : vérifie l'ownership
	/// directement par comparaison à <c>GameObject.Network.Owner</c> (pas via
	/// <see cref="KodokuPlayerComponent.FindByConnection"/>), puisque cette RPC cible directement le
	/// pawn de l'appelant. Ne fait confiance à aucune autre donnée du client que l'<c>InstanceId</c>
	/// sélectionné dans son propre snapshot local et le slot demandé — jamais une
	/// <see cref="ItemDefinition"/>, une position de grille, une rotation, une quantité, le contenu
	/// actuel du slot, ou un résultat attendu.
	/// </summary>
	[Rpc.Host]
	public void RequestEquip( string instanceId, EquipmentSlotType slot )
	{
		var caller = Rpc.Caller;

		if ( GameObject.Network.Owner != caller )
		{
			Log.Warning( $"[Equipment][Equip][Fail] '{caller?.DisplayName}' a demandé un équipement sur '{GameObject.Name}', dont il n'est pas le propriétaire — refusé." );
			return;
		}

		if ( !Guid.TryParse( instanceId, out var parsedId ) || parsedId == Guid.Empty )
		{
			Log.Warning( $"[Equipment][Equip][Fail] '{caller?.DisplayName}': InstanceId '{instanceId}' invalide." );
			return;
		}

		var result = TryEquipAuthoritative( caller, parsedId, slot );

		if ( result.Success )
			Log.Info( $"[Equipment][Equip][Success] '{caller?.DisplayName}' a équipé {parsedId} dans '{slot}' sur '{GameObject.Name}'." );
		else
			Log.Warning( $"[Equipment][Equip][Fail] '{caller?.DisplayName}' -> '{GameObject.Name}' ({slot}): {result.FailureReason}" );
	}

	/// <summary>
	/// Transaction métier complète, indépendante de tout transport réseau — même esprit que
	/// <see cref="Kodoku.Player.Inventory.PlayerItemDropComponent.TryDropAuthoritative"/>. Ordre
	/// strict : toutes les validations (ownership, identifiant, slot, existence, compatibilité,
	/// disponibilité) sont effectuées avant la moindre mutation ; une fois validées, le retrait de
	/// <see cref="Container"/> puis l'assignation à <see cref="_equippedSlots"/> ne peuvent plus
	/// échouer (la seconde est une simple écriture de dictionnaire) — voir
	/// docs/architecture/ITEM_ARCHITECTURE.md, section « Atomicité et rollback », pour la
	/// justification de l'absence de mécanisme de rollback dédié dans ce système. Ne crée jamais de
	/// nouvelle <see cref="ItemInstance"/> ni ne modifie son <c>InstanceId</c> : la même référence
	/// est simplement déplacée d'une structure host à l'autre.
	/// </summary>
	public EquipmentOperationResult TryEquipAuthoritative( Connection requester, Guid instanceId, EquipmentSlotType slot )
	{
		// Garde défensive — ne devrait jamais se produire pour un appel host réel, mais on ne mute
		// jamais l'état de gameplay sans avoir vérifié l'autorité, même ici (même patron que
		// PlayerItemDropComponent.TryDropAuthoritative).
		if ( !Networking.IsHost )
		{
			Log.Error( $"[Equipment][InternalError] TryEquipAuthoritative() exécuté hors du host pour '{GameObject.Name}'." );
			return EquipmentOperationResult.Fail( EquipmentFailureReason.InternalError );
		}

		if ( instanceId == Guid.Empty )
			return EquipmentOperationResult.Fail( EquipmentFailureReason.InvalidInstanceId );

		if ( slot != EquipmentSlotType.Head && slot != EquipmentSlotType.Body )
			return EquipmentOperationResult.Fail( EquipmentFailureReason.InvalidSlot );

		// 1. Résolution — l'appelant correspond-il à un pawn valide, et est-ce bien CE pawn ?
		var pawn = KodokuPlayerComponent.FindByConnection( Scene, requester );
		if ( pawn is null || pawn.PlayerController is null )
			return EquipmentOperationResult.Fail( EquipmentFailureReason.InvalidCaller );

		// Défense en profondeur : le pawn résolu depuis `requester` doit être exactement celui qui
		// porte ce composant — la RPC vérifie déjà GameObject.Network.Owner == Rpc.Caller, mais un
		// futur appelant direct de cette méthode (test déterministe) doit retrouver la même garantie.
		if ( pawn.GameObject != GameObject )
			return EquipmentOperationResult.Fail( EquipmentFailureReason.OwnershipRejected );

		if ( Container is null )
			return EquipmentOperationResult.Fail( EquipmentFailureReason.InvalidCaller );

		// 2. L'instance existe-t-elle dans la grille canonique ?
		var placement = Container.GetPlacement( instanceId );
		if ( placement is null )
			return EquipmentOperationResult.Fail( EquipmentFailureReason.ItemNotFound );

		var item = placement.Item;
		var definition = item.Definition;
		if ( definition is null || string.IsNullOrEmpty( definition.ItemId ) )
			return EquipmentOperationResult.Fail( EquipmentFailureReason.ItemNotFound );

		// 3. Compatibilité — jamais déduite de Category/Tags/nom, uniquement EquipmentSlot.
		if ( definition.EquipmentSlot == EquipmentSlotType.None )
			return EquipmentOperationResult.Fail( EquipmentFailureReason.ItemNotEquippable );

		if ( definition.EquipmentSlot != slot )
			return EquipmentOperationResult.Fail( EquipmentFailureReason.IncompatibleSlot );

		// 4. Disponibilité du slot — refus propre, jamais de swap automatique (décision V1).
		if ( _equippedSlots.ContainsKey( slot ) )
			return EquipmentOperationResult.Fail( EquipmentFailureReason.SlotOccupied );

		// 5. Toutes les validations ont réussi : le retrait puis l'assignation ne peuvent plus
		// échouer (TryRemove ne peut échouer que si l'instance a disparu entre les deux lignes
		// précédentes et celle-ci, impossible sous l'hypothèse d'un host mono-thread déjà actée par
		// LootSpawnPointComponent.HasEvaluated/WorldItemPickupComponent.IsClaimed).
		var removeResult = Container.TryRemove( instanceId, out var removedItem );
		if ( !removeResult.Success || removedItem != item )
		{
			Log.Error( $"[Equipment][InternalError] TryRemove a échoué après validation complète pour '{instanceId}' sur '{GameObject.Name}' — état canonique potentiellement incohérent." );
			return EquipmentOperationResult.Fail( EquipmentFailureReason.InternalError );
		}

		// 6. Assignation de la même référence ItemInstance — écriture de dictionnaire, infaillible.
		_equippedSlots[slot] = item;

		// 7. Succès confirmé — une seule révision, un seul snapshot combiné.
		NotifyMutated();
		return EquipmentOperationResult.Ok();
	}

	/// <summary>
	/// Point d'entrée réseau unique du déséquipement. Même schéma d'ownership que
	/// <see cref="RequestEquip"/>. Le client ne transmet que le slot à libérer.
	/// </summary>
	[Rpc.Host]
	public void RequestUnequip( EquipmentSlotType slot )
	{
		var caller = Rpc.Caller;

		if ( GameObject.Network.Owner != caller )
		{
			Log.Warning( $"[Equipment][Unequip][Fail] '{caller?.DisplayName}' a demandé un déséquipement sur '{GameObject.Name}', dont il n'est pas le propriétaire — refusé." );
			return;
		}

		var result = TryUnequipAuthoritative( caller, slot );

		if ( result.Success )
			Log.Info( $"[Equipment][Unequip][Success] '{caller?.DisplayName}' a déséquipé '{slot}' sur '{GameObject.Name}'." );
		else
			Log.Warning( $"[Equipment][Unequip][Fail] '{caller?.DisplayName}' -> '{GameObject.Name}' ({slot}): {result.FailureReason}" );
	}

	/// <summary>
	/// Transaction métier complète du déséquipement — ordre délibérément inverse d'un simple
	/// « vider le slot puis ajouter à la grille » : l'étape pouvant échouer
	/// (<see cref="InventoryContainer.TryAddFirstFit"/>, inventaire plein) s'exécute **avant**
	/// l'étape qui ne peut pas échouer (retirer une entrée d'un dictionnaire). Si l'ajout échoue,
	/// rien d'autre n'a encore été modifié : l'item reste équipé, aucune révision, aucun snapshot —
	/// voir docs/architecture/ITEM_ARCHITECTURE.md, section « Transaction — déséquiper ».
	/// </summary>
	public EquipmentOperationResult TryUnequipAuthoritative( Connection requester, EquipmentSlotType slot )
	{
		if ( !Networking.IsHost )
		{
			Log.Error( $"[Equipment][InternalError] TryUnequipAuthoritative() exécuté hors du host pour '{GameObject.Name}'." );
			return EquipmentOperationResult.Fail( EquipmentFailureReason.InternalError );
		}

		if ( slot != EquipmentSlotType.Head && slot != EquipmentSlotType.Body )
			return EquipmentOperationResult.Fail( EquipmentFailureReason.InvalidSlot );

		var pawn = KodokuPlayerComponent.FindByConnection( Scene, requester );
		if ( pawn is null || pawn.PlayerController is null )
			return EquipmentOperationResult.Fail( EquipmentFailureReason.InvalidCaller );

		if ( pawn.GameObject != GameObject )
			return EquipmentOperationResult.Fail( EquipmentFailureReason.OwnershipRejected );

		if ( Container is null )
			return EquipmentOperationResult.Fail( EquipmentFailureReason.InvalidCaller );

		if ( !_equippedSlots.TryGetValue( slot, out var item ) )
			return EquipmentOperationResult.Fail( EquipmentFailureReason.SlotEmpty );

		// Étape faillible en premier : tant que l'ajout n'a pas réussi, rien d'autre n'est modifié.
		var addResult = Container.TryAddFirstFit( item );
		if ( !addResult.Success )
		{
			return addResult.FailureReason == InventoryFailureReason.NoAvailableSpace
				? EquipmentOperationResult.Fail( EquipmentFailureReason.InventoryFull )
				: EquipmentOperationResult.Fail( EquipmentFailureReason.InternalError );
		}

		// Ajout confirmé : la libération du slot est une écriture de dictionnaire, infaillible.
		_equippedSlots.Remove( slot );

		NotifyMutated();
		return EquipmentOperationResult.Ok();
	}
}
