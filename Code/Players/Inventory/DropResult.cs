namespace Kodoku.Player.Inventory;

/// <summary>
/// Résultat explicite d'une tentative de drop — jamais un simple booléen, même pattern que
/// <see cref="Kodoku.Items.Interaction.PickupResult"/>. <see cref="FailureReason"/> précise la
/// cause d'un échec ; en cas de succès elle vaut <see cref="DropFailureReason.None"/>.
/// </summary>
public readonly record struct DropResult
{
	public bool Success { get; }

	public DropFailureReason FailureReason { get; }

	DropResult( bool success, DropFailureReason reason )
	{
		Success = success;
		FailureReason = reason;
	}

	public static DropResult Ok() => new( true, DropFailureReason.None );

	public static DropResult Fail( DropFailureReason reason ) => new( false, reason );
}
