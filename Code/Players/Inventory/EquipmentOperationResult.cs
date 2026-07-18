namespace Kodoku.Player.Inventory;

/// <summary>
/// Résultat explicite d'une tentative d'équiper/déséquiper — jamais un simple booléen, même
/// pattern que <see cref="DropResult"/>/<see cref="Kodoku.Items.Interaction.PickupResult"/>.
/// <see cref="FailureReason"/> précise la cause d'un échec ; en cas de succès elle vaut
/// <see cref="EquipmentFailureReason.None"/>. Ne porte ni slot ni <c>InstanceId</c> : l'appelant
/// (<c>RequestEquip</c>/<c>RequestUnequip</c>) les connaît déjà depuis ses propres paramètres pour
/// le logging — même choix que <see cref="DropResult"/>/<see cref="Kodoku.Items.Interaction.PickupResult"/>,
/// qui ne dupliquent pas non plus les paramètres d'entrée dans le résultat.
/// </summary>
public readonly record struct EquipmentOperationResult
{
	public bool Success { get; }

	public EquipmentFailureReason FailureReason { get; }

	EquipmentOperationResult( bool success, EquipmentFailureReason reason )
	{
		Success = success;
		FailureReason = reason;
	}

	public static EquipmentOperationResult Ok() => new( true, EquipmentFailureReason.None );

	public static EquipmentOperationResult Fail( EquipmentFailureReason reason ) => new( false, reason );
}
