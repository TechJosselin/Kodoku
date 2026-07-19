namespace Kodoku.World.Containers;

/// <summary>
/// Raison d'échec explicite d'un transfert whole-item (<see cref="WorldContainerComponent.RequestTakeItem"/>/
/// <see cref="WorldContainerComponent.RequestStoreItem"/>), déterminée exclusivement côté host —
/// jamais déduite d'un état côté client. Délibérément distincte de
/// <see cref="WorldContainerFailureReason"/> (session : ouverture/fermeture/resync) — voir le
/// commentaire de cet enum, qui exclut volontairement toute raison de transfert de son périmètre.
///
/// <see cref="DestinationNoSpace"/>, pas « DestinationFull » : <see cref="Kodoku.Items.Inventory.InventoryFailureReason.NoAvailableSpace"/>
/// ne permet pas de distinguer une grille pleine d'un item qui ne peut structurellement pas y
/// tenir (dimensions supérieures à la grille) — les deux causes retombent sur cette même valeur,
/// le nom reflète cette ambiguïté plutôt que de la masquer.
/// </summary>
public enum WorldContainerTransferFailureReason
{
	None,
	InvalidCaller,
	NotViewer,
	OutOfRange,
	InvalidInstanceId,
	ItemNotFound,
	DestinationNoSpace,
	RollbackFailed,
	InternalError,
}
