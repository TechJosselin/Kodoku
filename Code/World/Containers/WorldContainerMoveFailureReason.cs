namespace Kodoku.World.Containers;

/// <summary>
/// Raison d'échec explicite d'un déplacement interne (<see cref="WorldContainerComponent.RequestMoveItem"/>)
/// d'un item déjà présent dans ce conteneur, vers une nouvelle cellule/rotation au sein du même
/// conteneur — jamais un transfert entre deux conteneurs (voir <see cref="WorldContainerTransferFailureReason"/>
/// pour Take/Store). Déterminée exclusivement côté host — jamais déduite d'un état côté client. Même
/// pattern que <see cref="WorldContainerTransferFailureReason"/> : refus de gameplay normaux
/// (<see cref="OutOfBounds"/>/<see cref="Overlapping"/>/<see cref="RotationNotAllowed"/>) distingués
/// explicitement, jamais collapsés sur <see cref="InternalError"/>.
/// </summary>
public enum WorldContainerMoveFailureReason
{
	None,
	InvalidCaller,
	NotViewer,
	OutOfRange,
	InvalidInstanceId,
	ItemNotFound,

	/// <summary>
	/// La position/rotation observée par le client au début du drag ne correspond plus au placement
	/// canonique courant (une autre requête, éventuellement d'un autre viewer, a déjà déplacé cet item
	/// entre-temps) — voir <see cref="WorldContainerComponent.RequestMoveItem"/>, paramètres
	/// <c>expectedSourceX/Y/Rotated</c>. Refus de gameplay normal (concurrence), jamais collapsé sur
	/// <see cref="InternalError"/>.
	/// </summary>
	StaleSource,

	OutOfBounds,
	Overlapping,
	RotationNotAllowed,
	InternalError,
}
