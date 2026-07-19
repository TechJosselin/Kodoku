namespace Kodoku.World.Containers;

/// <summary>
/// Résultat explicite d'une tentative de transfert whole-item — jamais un simple booléen, même
/// patron que <see cref="WorldContainerOperationResult"/>/<see cref="Kodoku.Player.Inventory.DropResult"/>.
/// Volontairement minimal : ne porte ni <see cref="Kodoku.Items.ItemDefinition"/>, ni
/// <c>Quantity</c>, ni position, ni aucune référence de joueur ou de <see cref="Sandbox.Connection"/> — aucune
/// donnée canonique. C'est un accusé de traitement pour l'appelant (voir
/// <see cref="WorldContainerComponent.ReceiveTransferResult"/>), pas un second canal d'état
/// d'inventaire : le contenu réel voyage uniquement par les snapshots existants
/// (<see cref="WorldContainerSnapshotEntry"/>/<see cref="Kodoku.Player.Inventory.InventorySnapshotEntry"/>).
/// </summary>
public readonly record struct WorldContainerTransferResult
{
	public bool Success { get; }

	public WorldContainerTransferDirection Direction { get; }

	/// <summary>Représentation chaîne canonique (<c>Guid.ToString()</c>) — même convention que <see cref="WorldContainerSnapshotEntry.InstanceId"/>.</summary>
	public string InstanceId { get; }

	public WorldContainerTransferFailureReason FailureReason { get; }

	WorldContainerTransferResult( bool success, WorldContainerTransferDirection direction, string instanceId, WorldContainerTransferFailureReason failureReason )
	{
		Success = success;
		Direction = direction;
		InstanceId = instanceId;
		FailureReason = failureReason;
	}

	public static WorldContainerTransferResult Ok( WorldContainerTransferDirection direction, string instanceId )
		=> new( true, direction, instanceId, WorldContainerTransferFailureReason.None );

	public static WorldContainerTransferResult Fail( WorldContainerTransferDirection direction, string instanceId, WorldContainerTransferFailureReason failureReason )
		=> new( false, direction, instanceId, failureReason );
}
