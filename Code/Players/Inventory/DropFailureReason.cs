namespace Kodoku.Player.Inventory;

/// <summary>
/// Raison d'échec explicite d'une tentative de drop, déterminée exclusivement côté host —
/// jamais déduite d'un état côté client. Même pattern que
/// <see cref="Kodoku.Items.Interaction.PickupFailureReason"/>. Voir docs/architecture/ITEM_ARCHITECTURE.md.
/// </summary>
public enum DropFailureReason
{
	None,
	InvalidCaller,
	OwnershipRejected,
	InvalidInstanceId,
	ItemNotFound,
	InvalidDefinition,
	InvalidQuantity,
	MissingWorldPrefab,
	InvalidDropPosition,
	CloneFailed,
	MissingWorldItemComponent,
	NetworkSpawnFailed,
	WorldInitializationFailed,
	RollbackFailed,
	InternalError,
}
