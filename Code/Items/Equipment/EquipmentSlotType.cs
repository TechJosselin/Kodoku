namespace Kodoku.Items;

/// <summary>
/// Emplacements d'équipement corporel reconnus par cette V1 — volontairement minimal, voir
/// docs/architecture/ITEM_ARCHITECTURE.md, section « Équipement corporel minimal ». Sert à la
/// fois de valeur de compatibilité sur <see cref="ItemDefinition.EquipmentSlot"/> et de clé pour
/// l'état canonique d'équipement de <see cref="Kodoku.Player.Inventory.PlayerInventoryComponent"/>.
/// <see cref="None"/> signifie « non équipable » — valeur par défaut de
/// <see cref="ItemDefinition.EquipmentSlot"/>, jamais un slot cible valide pour une requête
/// d'équipement.
/// </summary>
public enum EquipmentSlotType
{
	None,
	Head,
	Body,
}
