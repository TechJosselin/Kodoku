namespace Kodoku.Items;

/// <summary>
/// Catégorie déclarative d'un <see cref="ItemDefinition"/>, utilisée pour le tri/filtrage
/// dans l'inspecteur et une future UI d'inventaire. Ne détermine aucun comportement de
/// gameplay par elle-même — voir docs/architecture/ITEM_ARCHITECTURE.md.
/// </summary>
public enum ItemCategory
{
	Miscellaneous,
	Consumable,
	Medical,
	Food,
	Equipment,
	Weapon,
	Ammunition,
	Tool,
	Resource,
	Key,
	Quest,
}
