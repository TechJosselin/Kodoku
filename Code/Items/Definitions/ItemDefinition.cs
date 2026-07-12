using System;

namespace Kodoku.Items;

/// <summary>
/// Données statiques partagées par tous les exemplaires d'un même type d'item — un fichier
/// <c>.item</c> par type d'objet. Ne porte aucun état runtime (quantité, durabilité,
/// position, propriétaire, réseau...) : cet état appartiendra à une future <c>ItemInstance</c>,
/// pas encore implémentée. Voir docs/architecture/ITEM_ARCHITECTURE.md.
/// </summary>
[AssetType( Name = "Item Definition", Extension = "item", Category = "Kodoku" )]
public sealed class ItemDefinition : GameResource
{
	// --- Identity ---

	/// <summary>
	/// Identifiant stable et unique de ce type d'item, indépendant du nom affiché et du
	/// chemin du fichier. Ne doit jamais être dérivé de <see cref="DisplayName"/>.
	/// </summary>
	[Group( "Identity" )]
	[Title( "Item ID" )]
	[Description( "Stable technical identifier. Must not change after release." )]
	[Property]
	public string ItemId { get; set; } = "";

	[Group( "Identity" )]
	[Property]
	public string DisplayName { get; set; } = "";

	[Group( "Identity" )]
	[TextArea]
	[Property]
	public string Description { get; set; } = "";

	[Group( "Identity" )]
	[Property]
	public ItemCategory Category { get; set; } = ItemCategory.Miscellaneous;

	[Group( "Identity" )]
	[Property]
	public ItemTags Tags { get; set; } = ItemTags.None;

	// --- Presentation ---

	[Group( "Presentation" )]
	[Property]
	public Texture Icon { get; set; }

	// --- Inventory ---

	int _gridWidth = 1;
	[Group( "Inventory" )]
	[Property]
	public int GridWidth
	{
		get => _gridWidth;
		set => _gridWidth = Math.Max( 1, value );
	}

	int _gridHeight = 1;
	[Group( "Inventory" )]
	[Property]
	public int GridHeight
	{
		get => _gridHeight;
		set => _gridHeight = Math.Max( 1, value );
	}

	/// <summary>
	/// Indique que cet item peut être tourné dans une grille d'inventaire. La rotation
	/// effective d'un exemplaire n'appartient pas à cette définition.
	/// </summary>
	[Group( "Inventory" )]
	[Property]
	public bool CanRotate { get; set; } = true;

	float _weight;
	/// <summary>Poids d'une unité, en kilogrammes.</summary>
	[Group( "Inventory" )]
	[Property]
	public float Weight
	{
		get => _weight;
		set => _weight = MathF.Max( 0f, value );
	}

	int _maxStack = 1;
	/// <summary>MaxStack = 1 → non empilable. MaxStack &gt; 1 → empilable.</summary>
	[Group( "Inventory" )]
	[Property]
	public int MaxStack
	{
		get => _maxStack;
		set => _maxStack = Math.Max( 1, value );
	}

	// --- World ---

	[Group( "World" )]
	[Property]
	public Model WorldModel { get; set; }

	/// <summary>
	/// Prefab optionnel à utiliser pour la représentation monde de cet item, si un rendu
	/// non trivial (au-delà de <see cref="WorldModel"/> seul) est nécessaire. Non consommé
	/// par aucun système actuellement — la représentation monde (WorldItemComponent)
	/// n'est pas encore implémentée.
	/// </summary>
	[Group( "World" )]
	[Property]
	public PrefabFile WorldPrefabOverride { get; set; }
}
