namespace Kodoku.Player.Inventory;

/// <summary>
/// Résultat explicite d'une tentative d'utilisation d'item — jamais un simple booléen, même pattern
/// que <see cref="EquipmentOperationResult"/>/<see cref="DropResult"/>. <see cref="FailureReason"/>
/// précise la cause d'un échec ; en cas de succès elle vaut <see cref="ItemUseFailureReason.None"/>.
/// Ne porte ni <c>InstanceId</c> ni <c>ItemId</c> : l'appelant (<see cref="PlayerItemUseComponent.RequestUse"/>)
/// les connaît déjà depuis ses propres paramètres pour le logging — même choix que
/// <see cref="EquipmentOperationResult"/>, qui ne duplique pas non plus les paramètres d'entrée dans
/// le résultat.
/// </summary>
public readonly record struct ItemUseOperationResult
{
	public bool Success { get; }

	public ItemUseFailureReason FailureReason { get; }

	ItemUseOperationResult( bool success, ItemUseFailureReason reason )
	{
		Success = success;
		FailureReason = reason;
	}

	public static ItemUseOperationResult Ok() => new( true, ItemUseFailureReason.None );

	public static ItemUseOperationResult Fail( ItemUseFailureReason reason ) => new( false, reason );
}
