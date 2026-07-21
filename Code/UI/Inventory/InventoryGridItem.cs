using Sandbox;

namespace Kodoku.UI;

/// <summary>
/// Modèle de présentation local pour une entrée affichée par <see cref="InventoryGrid"/> — jamais une
/// donnée métier ou réseau. Construit par le parent (<c>InventoryPage</c>) à chaque frame depuis un
/// snapshot déjà reçu (<c>InventorySnapshotEntry</c>/<c>WorldContainerSnapshotEntry</c>) et une
/// <c>ItemDefinition</c> résolue localement — <see cref="InventoryGrid"/> ne connaît que cette forme,
/// jamais <c>PlayerInventoryComponent</c> ou <c>WorldContainerComponent</c> directement. <see cref="Width"/>/
/// <see cref="Height"/> sont déjà la taille visuelle finale (grille inversée si <see cref="IsRotated"/>),
/// jamais les dimensions brutes de l'<c>ItemDefinition</c> — le parent effectue déjà cette inversion.
/// </summary>
public readonly record struct InventoryGridItem(
	string InstanceId,
	string ItemId,
	string DisplayName,
	Texture Icon,
	int Quantity,
	int X,
	int Y,
	int Width,
	int Height,
	bool IsRotated,
	bool IsPending,
	bool IsSelected
);
