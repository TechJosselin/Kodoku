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

	/// <summary>
	/// La position/rotation observée par le client au début du drag ne correspond plus au placement
	/// canonique courant de la source (un autre viewer, ou une autre requête du même client, a déjà
	/// déplacé cet item entre-temps) — voir <see cref="WorldContainerComponent.RequestStoreItemAt"/>/
	/// <see cref="WorldContainerComponent.RequestTakeItemAt"/>, paramètres
	/// <c>expectedSourceX/Y/Rotated</c>. Refus de gameplay normal (concurrence), jamais collapsé sur
	/// <see cref="InternalError"/>.
	/// </summary>
	StaleSource,

	DestinationNoSpace,

	/// <summary>
	/// Ajoutées pour le transfert ciblé (<see cref="WorldContainerComponent.RequestStoreItemAt"/>/
	/// <see cref="WorldContainerComponent.RequestTakeItemAt"/>, drag-and-drop V1) : un placement à une
	/// cellule précise, contrairement au premier-ajustement (<see cref="DestinationNoSpace"/> seul),
	/// peut échouer pour ces raisons distinctes — jamais atteignables par un scan first-fit. Distinguées
	/// explicitement plutôt que collapsées sur <see cref="InternalError"/> : ce sont des refus de
	/// gameplay normaux, pas des anomalies internes.
	/// </summary>
	OutOfBounds,
	Overlapping,
	RotationNotAllowed,

	RollbackFailed,
	InternalError,
}
