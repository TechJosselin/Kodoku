namespace Kodoku.Player.Inventory;

/// <summary>
/// Raison d'échec explicite d'une tentative d'équiper/déséquiper, déterminée exclusivement côté
/// host — jamais déduite d'un état côté client. Même pattern que
/// <see cref="Kodoku.Items.Interaction.PickupFailureReason"/>/<see cref="DropFailureReason"/>.
/// Pas de valeur <c>NotHost</c> dédiée : la garde défensive <c>!Networking.IsHost</c> retourne
/// <see cref="InternalError"/>, comme pour le pickup et le drop — ce cas n'est jamais un résultat
/// métier réel pour un appel host authentique. Pas de valeur <c>InventoryRejected</c> distincte :
/// <see cref="InventoryFull"/> couvre le cas métier réel (<c>InventoryFailureReason.NoAvailableSpace</c>),
/// toute autre <c>InventoryFailureReason</c> inattendue retombant sur <see cref="InternalError"/> —
/// même mapping que <see cref="Kodoku.Items.Interaction.PickupFailureReason"/> pour
/// <c>TryAddFirstFit</c>. Voir docs/architecture/ITEM_ARCHITECTURE.md.
/// </summary>
public enum EquipmentFailureReason
{
	None,
	InvalidCaller,
	OwnershipRejected,
	InvalidInstanceId,
	InvalidSlot,
	ItemNotFound,
	ItemNotEquippable,
	IncompatibleSlot,
	SlotOccupied,
	SlotEmpty,
	InventoryFull,
	InternalError,
}
