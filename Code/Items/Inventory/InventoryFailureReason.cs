namespace Kodoku.Items.Inventory;

/// <summary>
/// Raison d'échec explicite d'une opération de <see cref="InventoryContainer"/>. Une opération
/// de gameplay normalement refusée (case occupée, inventaire plein, rotation interdite, hors
/// limites) ne lève jamais d'exception — voir docs/architecture/ITEM_ARCHITECTURE.md.
/// </summary>
public enum InventoryFailureReason
{
	None,
	InvalidContainerSize,
	InvalidItem,
	InvalidDefinition,
	InvalidInstanceId,
	InvalidItemDimensions,
	InvalidQuantity,
	RotationNotAllowed,
	OutOfBounds,
	Overlapping,
	AlreadyContained,
	ItemNotFound,
	NoAvailableSpace,
}
