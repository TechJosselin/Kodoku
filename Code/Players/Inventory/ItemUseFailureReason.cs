namespace Kodoku.Player.Inventory;

/// <summary>
/// Raison d'échec explicite d'une tentative d'utilisation d'item, déterminée exclusivement côté
/// host — jamais déduite d'un état côté client. Même pattern que
/// <see cref="EquipmentFailureReason"/>/<see cref="DropFailureReason"/>. Pas de valeur distincte pour
/// « item équipé » : un item équipé n'est jamais présent dans <see cref="Kodoku.Items.Inventory.InventoryContainer"/>
/// (retiré par <see cref="PlayerInventoryComponent.TryEquipAuthoritative"/>), donc
/// <see cref="ItemNotFound"/> couvre déjà ce cas sans traitement spécial. Pas de valeur
/// <c>InvalidQuantity</c> distincte : le montant consommé est toujours 1, jamais fourni par le
/// client — un échec inattendu de <see cref="Kodoku.Items.Inventory.InventoryContainer.TryConsume"/>
/// retombe sur <see cref="InternalError"/>, même mapping que les autres domaines pour leurs échecs
/// internes imprévus.
/// </summary>
public enum ItemUseFailureReason
{
	None,
	InvalidCaller,
	OwnershipRejected,
	InvalidInstanceId,
	ItemNotFound,
	InvalidDefinition,
	NotUsable,
	NeedsComponentMissing,
	ThirstAlreadyFull,
	InternalError,
}
