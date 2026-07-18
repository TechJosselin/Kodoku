using Kodoku.Items;

namespace Kodoku.Player.Inventory;

/// <summary>
/// Représentation réseau en lecture seule d'un emplacement d'équipement — même convention que
/// <see cref="InventorySnapshotEntry"/> : <see cref="InstanceId"/> en chaîne canonique
/// (<c>Guid.ToString()</c>), aucune référence directe vers <see cref="ItemDefinition"/> ou
/// <see cref="ItemInstance"/>. Pas de <c>X</c>/<c>Y</c>/<c>IsRotated</c> : un emplacement
/// d'équipement n'est pas spatial. Envoyée dans le même snapshot combiné que
/// <see cref="InventorySnapshotEntry"/>, sous la même révision — voir
/// docs/architecture/ITEM_ARCHITECTURE.md, section « Équipement corporel minimal ».
/// <see cref="Quantity"/> vaut toujours 1 pour cette V1 (aucun item équipable empilable), conservé
/// par cohérence avec <see cref="InventorySnapshotEntry"/> — la quantité fait déjà partie du
/// modèle runtime de <see cref="ItemInstance"/>, sans coût supplémentaire à transmettre.
/// </summary>
public readonly record struct EquipmentSnapshotEntry(
	EquipmentSlotType Slot,
	string InstanceId,
	string ItemId,
	int Quantity
);
