namespace Kodoku.World.Containers;

/// <summary>
/// Représentation réseau en lecture seule d'un <see cref="Kodoku.Items.Inventory.InventoryPlacement"/>
/// appartenant à un <see cref="WorldContainerComponent"/> — jamais le placement lui-même, jamais une
/// référence vers un <see cref="Kodoku.Items.ItemDefinition"/> ou un <see cref="Kodoku.Items.ItemInstance"/>.
/// Décision explicite : ne réutilise pas <see cref="Kodoku.Player.Inventory.InventorySnapshotEntry"/>
/// (namespace conceptuellement lié au joueur, déjà validé par huit scénarios de test réel) — voir
/// docs/architecture/WORLD_CONTAINER_ARCHITECTURE.md, section 11. Mêmes six champs, mêmes conventions :
/// <see cref="InstanceId"/> est <see cref="System.Guid"/> converti en chaîne canonique
/// (<c>Guid.ToString()</c>), aucun précédent confirmé de support natif de <see cref="System.Guid"/> par
/// les RPC/<c>[Sync]</c> dans ce projet.
/// </summary>
public readonly record struct WorldContainerSnapshotEntry(
	string InstanceId,
	string ItemId,
	int Quantity,
	int X,
	int Y,
	bool IsRotated
);
