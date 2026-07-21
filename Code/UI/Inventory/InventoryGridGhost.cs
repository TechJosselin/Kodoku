namespace Kodoku.UI;

/// <summary>
/// Aperçu fantôme affiché par <see cref="InventoryGrid"/> pendant un drag-and-drop — présentation
/// pure, calculée et validée par le parent (<c>InventoryPage</c>, seul à connaître le snapshot local
/// et donc la validité indicative d'une cellule cible). <see cref="InventoryGrid"/> ne fait que
/// positionner/dessiner ce rectangle, jamais ne décide de sa validité — voir mission section 13
/// (InventoryGrid reste agnostique de tout ce qui est métier/réseau).
/// </summary>
public readonly record struct InventoryGridGhost(
	int X,
	int Y,
	int Width,
	int Height,
	bool IsValid
);
