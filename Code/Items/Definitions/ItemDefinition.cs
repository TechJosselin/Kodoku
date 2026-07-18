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

	// --- Equipment ---

	/// <summary>
	/// Emplacement d'équipement compatible, ou <see cref="EquipmentSlotType.None"/> si cet item
	/// n'est pas équipable — valeur par défaut, aucune modification nécessaire pour un item
	/// existant non équipable (ex. <c>water_bottle</c>). Propriété dédiée, jamais déduite de
	/// <see cref="Category"/> (qui contient déjà une valeur <c>Equipment</c> trop grossière — un
	/// casque et une arme partagent cette catégorie sans être interchangeables), de
	/// <see cref="Tags"/>, de <see cref="DisplayName"/>, de <see cref="ItemId"/>, du chemin de
	/// fichier ou du prefab monde. Voir docs/architecture/ITEM_ARCHITECTURE.md.
	/// </summary>
	[Group( "Equipment" )]
	[Property]
	public EquipmentSlotType EquipmentSlot { get; set; } = EquipmentSlotType.None;

	// --- World ---

	[Group( "World" )]
	[Property]
	public Model WorldModel { get; set; }

	/// <summary>
	/// Prefab optionnel à utiliser pour la représentation monde de cet item, si un rendu
	/// non trivial (au-delà de <see cref="WorldModel"/> seul) est nécessaire. Consommé par
	/// <see cref="LootSpawnPointComponent"/> (génération de loot) et par
	/// <see cref="Kodoku.Player.Inventory.PlayerItemDropComponent"/> (drop) — tous deux
	/// clonent ce prefab puis le networkent ; son <c>WorldItemComponent</c> porte l'identité
	/// autoritaire de l'exemplaire matérialisé.
	/// </summary>
	[Group( "World" )]
	[Property]
	public PrefabFile WorldPrefabOverride { get; set; }
}
