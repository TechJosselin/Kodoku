using System;
using Kodoku.Items;
using Kodoku.Items.Inventory;
using Kodoku.Player;
using Kodoku.Player.Vitals;

namespace Kodoku.Player.Inventory;

/// <summary>
/// Utilise un consommable depuis l'inventaire canonique du host — orchestration à cheval sur deux
/// domaines (inventaire et vitals), donc portée par un composant séparé plutôt qu'intégrée à
/// <see cref="PlayerInventoryComponent"/> (qui reste seul propriétaire de <see cref="PlayerInventoryComponent.Container"/>,
/// de la révision et du snapshot). Même schéma que <see cref="PlayerItemDropComponent"/> : la RPC
/// cible directement le propre pawn de l'appelant, jamais un <c>Connection</c> résolu depuis un
/// objet tiers. V1 volontairement limitée à un seul effet (restauration de soif) — pas de système
/// générique d'effets, pas de faim/soin/stamina. Voir docs/architecture/ITEM_ARCHITECTURE.md.
///
/// <see cref="RequestUse"/> (transport, <c>[Rpc.Host]</c>) reste séparée de
/// <see cref="TryUseAuthoritative"/> (transaction métier, non-RPC, prend une <see cref="Connection"/>
/// explicite) — même séparation que le drop et l'équipement, pour un futur outil de test déterministe.
/// </summary>
public sealed class PlayerItemUseComponent : Component
{
	/// <summary>
	/// Résolu sur le même GameObject que <see cref="PlayerItemDropComponent"/>/<see cref="PlayerVitalsComponent"/>
	/// (racine de <c>kodoku_player.prefab</c>) — accès direct au <see cref="PlayerInventoryComponent.Container"/>
	/// canonique, jamais via un snapshot côté client.
	/// </summary>
	[RequireComponent]
	public PlayerInventoryComponent Inventory { get; set; }

	/// <summary>Résolu sur le même GameObject — seul lecteur/mutateur autorisé de la soif pour cette transaction.</summary>
	[RequireComponent]
	public PlayerVitalsComponent Vitals { get; set; }

	/// <summary>
	/// Point d'entrée réseau unique de l'utilisation. <c>[Rpc.Host]</c>, cible le propre pawn de
	/// l'appelant (même schéma que <see cref="PlayerItemDropComponent.RequestDrop"/>) : la vérification
	/// d'ownership se fait directement par comparaison à <c>GameObject.Network.Owner</c>, pas via
	/// <see cref="KodokuPlayerComponent.FindByConnection"/> — un client ne peut donc jamais faire
	/// utiliser un item par le pawn d'un autre joueur en ciblant son <see cref="PlayerItemUseComponent"/>.
	/// Ne fait confiance à aucune autre donnée du client que l'<c>InstanceId</c> sélectionné dans son
	/// propre snapshot local — jamais une définition, un effet ou un résultat attendu.
	/// </summary>
	[Rpc.Host]
	public void RequestUse( string instanceId )
	{
		var caller = Rpc.Caller;

		if ( GameObject.Network.Owner != caller )
		{
			Log.Warning( $"[ItemUse][Fail] '{caller?.DisplayName}' a demandé une utilisation sur '{GameObject.Name}', dont il n'est pas le propriétaire — refusé." );
			return;
		}

		if ( !Guid.TryParse( instanceId, out var parsedId ) || parsedId == Guid.Empty )
		{
			Log.Warning( $"[ItemUse][Fail] '{caller?.DisplayName}': InstanceId '{instanceId}' invalide." );
			return;
		}

		var result = TryUseAuthoritative( caller, parsedId );

		if ( result.Success )
			Log.Info( $"[ItemUse][Success] '{caller?.DisplayName}' a utilisé {parsedId} sur '{GameObject.Name}'." );
		else
			Log.Warning( $"[ItemUse][Fail] '{caller?.DisplayName}' -> '{GameObject.Name}': {result.FailureReason}" );
	}

	/// <summary>
	/// Transaction métier complète, indépendante de tout transport réseau — même esprit que
	/// <see cref="PlayerItemDropComponent.TryDropAuthoritative"/>/<see cref="PlayerInventoryComponent.TryEquipAuthoritative"/>.
	/// Ordre strict : toutes les validations (ownership, identifiant, existence, définition,
	/// utilisabilité, disponibilité de l'effet) sont effectuées avant la moindre mutation. Une fois
	/// validées, la consommation de <see cref="PlayerInventoryComponent.Container"/> est tentée en
	/// premier — c'est la mutation la plus complexe des deux (arithmétique de grille) ; si elle échoue
	/// de façon inattendue (ne devrait jamais se produire, même hypothèse mono-thread host que
	/// <see cref="Kodoku.Items.Interaction.WorldItemPickupComponent.IsClaimed"/>), rien d'autre n'a
	/// encore été modifié — la soif n'est restaurée qu'après confirmation de la consommation, jamais
	/// avant, pour qu'aucun échec inattendu ne puisse laisser une soif partiellement restaurée sans
	/// consommation correspondante. Ne crée jamais de nouvelle <see cref="ItemInstance"/> ni ne modifie
	/// son <c>InstanceId</c> — voir <see cref="InventoryContainer.TryConsume"/> pour l'invariant
	/// « Quantity ne peut jamais valoir zéro ».
	/// </summary>
	public ItemUseOperationResult TryUseAuthoritative( Connection requester, Guid instanceId )
	{
		// Garde défensive — ne devrait jamais se produire pour un appel host réel, mais on ne mute
		// jamais l'état de gameplay sans avoir vérifié l'autorité, même ici (même patron que
		// WorldItemPickupComponent.TryPickupAuthoritative).
		if ( !Networking.IsHost )
		{
			Log.Error( $"[ItemUse][InternalError] TryUseAuthoritative() exécuté hors du host pour '{GameObject.Name}'." );
			return ItemUseOperationResult.Fail( ItemUseFailureReason.InternalError );
		}

		if ( instanceId == Guid.Empty )
			return ItemUseOperationResult.Fail( ItemUseFailureReason.InvalidInstanceId );

		// 1. Résolution — l'appelant correspond-il à un pawn valide, et est-ce bien CE pawn ?
		var pawn = KodokuPlayerComponent.FindByConnection( Scene, requester );
		if ( pawn is null || pawn.PlayerController is null )
			return ItemUseOperationResult.Fail( ItemUseFailureReason.InvalidCaller );

		// Défense en profondeur : le pawn résolu depuis `requester` doit être exactement celui qui
		// porte ce composant — la RPC vérifie déjà GameObject.Network.Owner == Rpc.Caller, mais un
		// futur appelant direct de cette méthode (test déterministe) doit retrouver la même garantie.
		if ( pawn.GameObject != GameObject )
			return ItemUseOperationResult.Fail( ItemUseFailureReason.OwnershipRejected );

		// État interne invalide (Inventory/Container absents sur un pawn autrement valide), pas une
		// question d'identité de l'appelant — InternalError, pas InvalidCaller.
		if ( Inventory is null || Inventory.Container is null )
		{
			Log.Error( $"[ItemUse][InternalError] Inventory/Container absent sur '{GameObject.Name}' pour un pawn pourtant valide." );
			return ItemUseOperationResult.Fail( ItemUseFailureReason.InternalError );
		}

		// 2. L'instance existe-t-elle dans la grille canonique ? Un item équipé n'y est jamais présent
		// (retiré par TryEquipAuthoritative) — ItemNotFound couvre donc déjà ce cas, sans vérification
		// séparée.
		var placement = Inventory.Container.GetPlacement( instanceId );
		if ( placement is null )
			return ItemUseOperationResult.Fail( ItemUseFailureReason.ItemNotFound );

		var item = placement.Item;
		var definition = item.Definition;
		if ( definition is null || string.IsNullOrEmpty( definition.ItemId ) )
			return ItemUseOperationResult.Fail( ItemUseFailureReason.InvalidDefinition );

		// 3. Utilisabilité — jamais déduite de l'ItemId, uniquement de ThirstRestoreAmount (V1 à un
		// seul effet, voir ItemDefinition.ThirstRestoreAmount).
		if ( !float.IsFinite( definition.ThirstRestoreAmount ) || definition.ThirstRestoreAmount <= 0f )
			return ItemUseOperationResult.Fail( ItemUseFailureReason.NotUsable );

		if ( Vitals is null )
		{
			Log.Error( $"[ItemUse][InternalError] PlayerVitalsComponent absent sur '{GameObject.Name}'." );
			return ItemUseOperationResult.Fail( ItemUseFailureReason.NeedsComponentMissing );
		}

		// 4. L'effet aurait-il seulement un impact observable ?
		if ( !Vitals.CanRestoreThirst )
			return ItemUseOperationResult.Fail( ItemUseFailureReason.ThirstAlreadyFull );

		// 5. Toutes les validations ont réussi. Consommation d'abord (mutation la plus complexe) :
		// si elle échoue de façon inattendue, aucune autre mutation n'a lieu (voir commentaire de
		// méthode ci-dessus).
		var consumeResult = Inventory.Container.TryConsume( instanceId, 1 );
		if ( !consumeResult.Success )
		{
			Log.Error( $"[ItemUse][InternalError] TryConsume a échoué après validation complète pour '{instanceId}' sur '{GameObject.Name}' — état canonique potentiellement incohérent." );
			return ItemUseOperationResult.Fail( ItemUseFailureReason.InternalError );
		}

		// 6. Effet — infaillible (clamp interne à Vitals.RestoreThirst).
		Vitals.RestoreThirst( definition.ThirstRestoreAmount );

		// 7. Succès confirmé — une seule révision, un seul snapshot d'inventaire.
		Inventory.NotifyMutated();
		return ItemUseOperationResult.Ok();
	}
}
