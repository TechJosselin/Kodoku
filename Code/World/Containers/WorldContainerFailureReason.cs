namespace Kodoku.World.Containers;

/// <summary>
/// Raison d'échec explicite d'une opération de <see cref="WorldContainerComponent"/>, déterminée
/// exclusivement côté host — jamais déduite d'un état côté client. Même pattern que
/// <see cref="Kodoku.Items.Interaction.PickupFailureReason"/>/<see cref="Kodoku.Player.Inventory.DropFailureReason"/>.
/// Ne contient volontairement aucune raison liée aux transferts (hors périmètre de cette V1 —
/// voir docs/architecture/WORLD_CONTAINER_ARCHITECTURE.md).
/// </summary>
public enum WorldContainerFailureReason
{
	None,
	InvalidCaller,
	NotViewer,
	OutOfRange,
	ContainerUnavailable,
	InternalError,
}
