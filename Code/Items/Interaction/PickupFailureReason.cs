namespace Kodoku.Items.Interaction;

/// <summary>
/// Raison d'échec explicite d'une tentative de pickup, déterminée exclusivement côté host —
/// jamais déduite d'un résultat local de raycast. Voir docs/architecture/ITEM_ARCHITECTURE.md.
/// </summary>
public enum PickupFailureReason
{
	None,
	InvalidCaller,
	ObjectNotFound,
	OutOfRange,
	LineOfSightBlocked,
	AlreadyClaimed,
	InvalidDefinition,
	InventoryFull,
	InternalError,
}
