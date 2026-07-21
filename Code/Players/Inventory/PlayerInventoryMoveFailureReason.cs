namespace Kodoku.Player.Inventory;

/// <summary>
/// Raison d'échec explicite d'un déplacement interne (<see cref="PlayerInventoryComponent.RequestMoveItem"/>)
/// d'un item déjà présent dans la grille canonique du pawn, vers une nouvelle cellule/rotation au sein
/// de cette même grille — jamais un transfert vers/depuis un conteneur monde (voir
/// <see cref="Kodoku.World.Containers.WorldContainerTransferFailureReason"/> pour Take/Store). Même
/// pattern que <see cref="EquipmentFailureReason"/>/<see cref="DropFailureReason"/> : déterminée
/// exclusivement côté host, jamais déduite d'un état côté client.
/// </summary>
public enum PlayerInventoryMoveFailureReason
{
	None,
	InvalidCaller,
	OwnershipRejected,
	InvalidInstanceId,
	ItemNotFound,

	/// <summary>
	/// La position/rotation observée par le client au début du drag ne correspond plus au placement
	/// canonique courant (une autre requête a déjà déplacé cet item entre-temps) — voir
	/// <see cref="PlayerInventoryComponent.RequestMoveItem"/>, paramètres <c>expectedSourceX/Y/Rotated</c>.
	/// Refus de gameplay normal (concurrence), jamais collapsé sur <see cref="InternalError"/>.
	/// </summary>
	StaleSource,

	OutOfBounds,
	Overlapping,
	RotationNotAllowed,
	InternalError,
}
