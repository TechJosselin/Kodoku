namespace Kodoku.Player.Inventory;

/// <summary>
/// Représentation réseau en lecture seule d'un <see cref="Kodoku.Items.Inventory.InventoryPlacement"/> —
/// jamais le placement lui-même, jamais une référence vers un <see cref="Kodoku.Items.ItemDefinition"/>
/// ou un <see cref="Kodoku.Items.ItemInstance"/>. Un tableau de ces entrées est envoyé tel quel comme
/// paramètre RPC (voir <see cref="PlayerInventoryComponent.ReceiveSnapshot"/>) — types volontairement
/// limités aux primitifs/<see cref="string"/> pour rester dans le sous-ensemble documenté comme supporté
/// par les RPC du moteur (mêmes types que <c>[Sync]</c> : value types, <see cref="string"/>).
/// <see cref="InstanceId"/> est <see cref="Kodoku.Items.ItemInstance.InstanceId"/> converti en chaîne
/// (<c>Guid.ToString()</c>), même convention que <c>WorldItemComponent.NetworkInstanceId</c> — aucun
/// précédent confirmé de support natif de <see cref="System.Guid"/> par les RPC/<c>[Sync]</c> dans ce projet.
/// </summary>
public readonly record struct InventorySnapshotEntry(
	string InstanceId,
	string ItemId,
	int Quantity,
	int X,
	int Y,
	bool IsRotated
);
