using System;
using System.Linq;
using Kodoku.Items;
using Kodoku.Items.Inventory;

namespace Kodoku.Debugging;

/// <summary>
/// OUTIL TEMPORAIRE — valide manuellement <see cref="InventoryStackTransactions"/> et la nouvelle
/// primitive <see cref="InventoryContainer.TryGrowQuantity"/> (Jalon 1 « Stack Core pur »,
/// docs/architecture/INVENTORY_STACK_ARCHITECTURE.md). Mêmes conventions que
/// <see cref="InventoryCoreDebugComponent"/> : pas de réseau, pas de système natif d'inventaire, à
/// installer manuellement sous <c>_Debug</c> dans <c>GameplayTest.scene</c>, jamais commité une
/// fois la scène modifiée pour ce test.
///
/// Construit ses <see cref="ItemDefinition"/> entièrement en mémoire — aucune ressource
/// <c>.item</c> créée pour ce jalon, conformément à la mission : un item empilable
/// (<c>kodoku.debug.stackable</c>, MaxStack=10), un item incompatible (ItemId distinct, pour les
/// refus de compatibilité) et un item non empilable (MaxStack=1, pour les cas structurels
/// « MaxStack==1 »).
/// </summary>
public sealed class InventoryStackTransactionsDebugComponent : Component
{
	[Group( "Configuration" )]
	[Property]
	public bool RunTestsOnStart { get; set; } = true;

	bool _hasRun;

	ItemDefinition _stackable;
	ItemDefinition _incompatible;
	ItemDefinition _nonStackable;

	protected override void OnStart()
	{
		if ( !RunTestsOnStart || _hasRun )
			return;

		_hasRun = true;
		RunStackTransactionTests();
	}

	public void RunStackTransactionTests()
	{
		_stackable = new ItemDefinition { ItemId = "kodoku.debug.stackable", DisplayName = "Debug Stackable", GridWidth = 1, GridHeight = 1, CanRotate = false, Weight = 0.25f, MaxStack = 10 };
		_incompatible = new ItemDefinition { ItemId = "kodoku.debug.other", DisplayName = "Debug Other", GridWidth = 1, GridHeight = 1, CanRotate = false, Weight = 0.1f, MaxStack = 10 };
		_nonStackable = new ItemDefinition { ItemId = "kodoku.debug.single", DisplayName = "Debug Single", GridWidth = 1, GridHeight = 1, CanRotate = false, Weight = 0.5f, MaxStack = 1 };

		// Régression InventoryContainer — smoke test seulement, le noyau lui-même reste validé en
		// profondeur par InventoryCoreDebugComponent (Tests A à O).
		Report( "Reg-01", Test_Regression_AddFirstFitMove() );
		Report( "Reg-02", Test_Regression_RemoveOverlapRotation() );
		Report( "Reg-03", Test_Regression_ConsumePartialAndTotal() );
		Report( "Reg-04", Test_Regression_FailFactoryRejectsNone() );

		// Split
		Report( "S-01", Test_S01_SplitValid() );
		Report( "S-02", Test_S02_SplitZeroAmount() );
		Report( "S-03", Test_S03_SplitNegativeAmount() );
		Report( "S-04", Test_S04_SplitAmountEqualsSource() );
		Report( "S-05", Test_S05_SplitAmountGreaterThanSource() );
		Report( "S-06", Test_S06_SplitMaxStackOne() );
		Report( "S-07", Test_S07_SplitDestinationOutOfBounds() );
		Report( "S-08", Test_S08_SplitDestinationOccupied() );
		Report( "S-09", Test_S09_SplitRotationNotAllowed() );
		Report( "S-10", Test_S10_SplitPreparedWrongDefinition() );
		Report( "S-11", Test_S11_SplitPreparedWrongQuantity() );
		Report( "S-12", Test_S12_SplitPreparedInstanceIdAlreadyPresent() );
		Report( "S-13", Test_S13_SplitSourceIdentityPreserved() );
		Report( "S-14", Test_S14_SplitNewIdentityDistinct() );
		Report( "S-15", Test_S15_SplitWeightConserved() );
		Report( "S-16", Test_S16_SplitNoMutationOnFailure() );

		// Merge exact
		Report( "M-01", Test_M01_MergePartialValid() );
		Report( "M-02", Test_M02_MergeTotalValid() );
		Report( "M-03", Test_M03_MergeSameSourceAndTarget() );
		Report( "M-04", Test_M04_MergeIncompatibleStacks() );
		Report( "M-05", Test_M05_MergeTargetFull() );
		Report( "M-06", Test_M06_MergeZeroAmount() );
		Report( "M-07", Test_M07_MergeAmountGreaterThanSource() );
		Report( "M-08", Test_M08_MergeInsufficientTargetCapacity() );
		Report( "M-09", Test_M09_MergeSourceMissing() );
		Report( "M-10", Test_M10_MergeTargetMissing() );
		Report( "M-11", Test_M11_MergeTargetIdentityPreserved() );
		Report( "M-12", Test_M12_MergePartialSourceIdentityPreserved() );
		Report( "M-13", Test_M13_MergeTotalSourceIdentityRemoved() );
		Report( "M-14", Test_M14_MergeNoMutationOnFailure() );
		Report( "M-15", Test_M15_MergeSourceNotStackable() );
		Report( "M-16", Test_M16_MergeSameMissingIdReportsSourceNotFound() );

		// Merge jusqu'à capacité
		Report( "F-01", Test_F01_SourceSmallerThanCapacity() );
		Report( "F-02", Test_F02_SourceGreaterThanCapacity() );
		Report( "F-03", Test_F03_MovedAmountExact() );
		Report( "F-04", Test_F04_TargetFull() );
		Report( "F-05", Test_F05_MergeTotal() );
		Report( "F-06", Test_F06_MergePartial() );
		Report( "F-07", Test_F07_UntilCapacitySuccessRequestedAmountIsZero() );
		Report( "F-08", Test_F08_UntilCapacityFailureRequestedAmountIsZero() );

		// Transfert partiel vers une case vide
		Report( "T-01", Test_T01_TransferValidCrossContainer() );
		Report( "T-02", Test_T02_SplitViaTransferSameContainer() );
		Report( "T-03", Test_T03_FullSourceAmountRejected() );
		Report( "T-04", Test_T04_DestinationOccupied() );
		Report( "T-05", Test_T05_DestinationOutOfBounds() );
		Report( "T-06", Test_T06_PreparedInstanceInvalid() );
		Report( "T-07", Test_T07_NewIdentityInTarget() );
		Report( "T-08", Test_T08_SourceIdentityPreserved() );
		Report( "T-09", Test_T09_WeightMovedExactly() );
		Report( "T-10", Test_T10_TotalWeightConserved() );
		Report( "T-11", Test_T11_NoMutationOnFailure() );
		Report( "T-12", Test_T12_TargetContainerNull() );

		// Merge entre deux conteneurs
		Report( "C-01", Test_C01_MergePartialCrossContainer() );
		Report( "C-02", Test_C02_MergeTotalCrossContainer() );
		Report( "C-03", Test_C03_TargetFull() );
		Report( "C-04", Test_C04_IncompatibleStacks() );
		Report( "C-05", Test_C05_SourceRemovedOnlyOnFullMerge() );
		Report( "C-06", Test_C06_WeightMovedCorrectly() );
		Report( "C-07", Test_C07_NoDuplication() );
		Report( "C-08", Test_C08_NoLoss() );

		// Atomicité — séquence combinée (voir rapport de mission pour la limite honnête sur le
		// chemin de rollback de la seconde mutation, non provocable sans API dangereuse dédiée).
		Report( "G-01", Test_G01_ComboSequenceInvariants() );
	}

	static void Report( string testId, (bool success, string reason) outcome )
	{
		if ( outcome.success )
			Log.Info( $"[StackTransactionsTest][{testId}][PASS]" );
		else
			Log.Warning( $"[StackTransactionsTest][{testId}][FAIL] {outcome.reason}" );
	}

	InventoryContainer NewContainer( int size = 4 ) => new( size, size );
	ItemInstance Stack( int qty ) => ItemInstance.CreateNew( _stackable, qty );
	ItemInstance Other( int qty ) => ItemInstance.CreateNew( _incompatible, qty );
	ItemInstance Single() => ItemInstance.CreateNew( _nonStackable, 1 );
	static bool NearlyEqual( float a, float b ) => MathF.Abs( a - b ) < 0.0001f;

	// --- Régression ---

	(bool, string) Test_Regression_AddFirstFitMove()
	{
		var container = NewContainer();
		var a = Stack( 1 );
		var addResult = container.TryAdd( a, 0, 0 );
		if ( !addResult.Success )
			return (false, $"TryAdd failed: {addResult.FailureReason}");

		var b = Stack( 1 );
		var fitResult = container.TryAddFirstFit( b );
		if ( !fitResult.Success || fitResult.Placement.X != 1 || fitResult.Placement.Y != 0 )
			return (false, "TryAddFirstFit did not land on the expected first free cell.");

		var moveResult = container.TryMove( a.InstanceId, 2, 0, rotated: false );
		if ( !moveResult.Success )
			return (false, $"TryMove failed: {moveResult.FailureReason}");

		return (true, null);
	}

	(bool, string) Test_Regression_RemoveOverlapRotation()
	{
		var container = NewContainer();
		var a = Stack( 1 );
		container.TryAdd( a, 0, 0 );

		var overlap = container.TryAdd( Stack( 1 ), 0, 0 );
		if ( overlap.Success || overlap.FailureReason != InventoryFailureReason.Overlapping )
			return (false, $"Expected Overlapping, got Success={overlap.Success} Reason={overlap.FailureReason}");

		var rotate = container.CanPlace( Stack( 1 ), 1, 0, rotated: true );
		if ( rotate.Success || rotate.FailureReason != InventoryFailureReason.RotationNotAllowed )
			return (false, $"Expected RotationNotAllowed, got Success={rotate.Success} Reason={rotate.FailureReason}");

		var removeResult = container.TryRemove( a.InstanceId, out var removed );
		if ( !removeResult.Success || !ReferenceEquals( removed, a ) )
			return (false, "TryRemove failed or returned a mismatched instance.");

		return (true, null);
	}

	(bool, string) Test_Regression_ConsumePartialAndTotal()
	{
		var container = NewContainer();
		var item = Stack( 5 );
		container.TryAdd( item, 0, 0 );

		var partial = container.TryConsume( item.InstanceId, 2 );
		if ( !partial.Success || item.Quantity != 3 || !container.Contains( item.InstanceId ) )
			return (false, "Partial TryConsume did not decrement in place as expected.");

		var total = container.TryConsume( item.InstanceId, 3 );
		if ( !total.Success || container.Contains( item.InstanceId ) )
			return (false, "Total TryConsume did not remove the placement as expected.");

		return (true, null);
	}

	(bool, string) Test_Regression_FailFactoryRejectsNone()
	{
		try
		{
			StackTransactionResult.Fail( StackTransactionFailureReason.None, Guid.NewGuid(), null, 0 );
			return (false, "Expected StackTransactionResult.Fail(None, ...) to throw an ArgumentException.");
		}
		catch ( ArgumentException )
		{
			return (true, null);
		}
	}

	// --- Split ---

	(bool, string) Test_S01_SplitValid()
	{
		var container = NewContainer();
		var source = Stack( 5 );
		container.TryAdd( source, 0, 0 );
		var prepared = Stack( 2 );

		var result = InventoryStackTransactions.TrySplit( container, source.InstanceId, 2, prepared );
		if ( !result.Success )
			return (false, $"TrySplit failed: {result.FailureReason}");

		if ( result.NewInstanceId != prepared.InstanceId )
			return (false, "NewInstanceId does not match the prepared instance.");

		if ( source.Quantity != 3 )
			return (false, $"Expected source Quantity=3, got {source.Quantity}");

		if ( prepared.Quantity != 2 )
			return (false, $"Expected prepared Quantity=2, got {prepared.Quantity}");

		if ( !container.Contains( source.InstanceId ) || !container.Contains( prepared.InstanceId ) )
			return (false, "Both the source and the extracted stack should remain in the container.");

		return (true, null);
	}

	(bool, string) Test_S02_SplitZeroAmount()
	{
		var container = NewContainer();
		var source = Stack( 5 );
		container.TryAdd( source, 0, 0 );

		var result = InventoryStackTransactions.TrySplit( container, source.InstanceId, 0, Stack( 1 ) );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.InvalidAmount )
			return (false, $"Expected InvalidAmount, got Success={result.Success} Reason={result.FailureReason}");

		if ( source.Quantity != 5 || container.Count != 1 )
			return (false, "Source was mutated despite a rejected zero-amount split.");

		return (true, null);
	}

	(bool, string) Test_S03_SplitNegativeAmount()
	{
		var container = NewContainer();
		var source = Stack( 5 );
		container.TryAdd( source, 0, 0 );

		var result = InventoryStackTransactions.TrySplit( container, source.InstanceId, -1, Stack( 1 ) );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.InvalidAmount )
			return (false, $"Expected InvalidAmount, got Success={result.Success} Reason={result.FailureReason}");

		if ( source.Quantity != 5 || container.Count != 1 )
			return (false, "Source was mutated despite a rejected negative-amount split.");

		return (true, null);
	}

	(bool, string) Test_S04_SplitAmountEqualsSource()
	{
		var container = NewContainer();
		var source = Stack( 5 );
		container.TryAdd( source, 0, 0 );

		var result = InventoryStackTransactions.TrySplit( container, source.InstanceId, 5, Stack( 5 ) );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.InvalidAmount )
			return (false, $"Expected InvalidAmount, got Success={result.Success} Reason={result.FailureReason}");

		if ( source.Quantity != 5 || container.Count != 1 )
			return (false, "Source was mutated despite amount == Quantity being rejected.");

		return (true, null);
	}

	(bool, string) Test_S05_SplitAmountGreaterThanSource()
	{
		var container = NewContainer();
		var source = Stack( 5 );
		container.TryAdd( source, 0, 0 );

		var result = InventoryStackTransactions.TrySplit( container, source.InstanceId, 6, Stack( 6 ) );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.InvalidAmount )
			return (false, $"Expected InvalidAmount, got Success={result.Success} Reason={result.FailureReason}");

		if ( source.Quantity != 5 || container.Count != 1 )
			return (false, "Source was mutated despite amount > Quantity being rejected.");

		return (true, null);
	}

	(bool, string) Test_S06_SplitMaxStackOne()
	{
		var container = NewContainer();
		var source = Single();
		container.TryAdd( source, 0, 0 );

		var result = InventoryStackTransactions.TrySplit( container, source.InstanceId, 1, Single() );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.InvalidAmount )
			return (false, $"Expected InvalidAmount, got Success={result.Success} Reason={result.FailureReason}");

		if ( source.Quantity != 1 || container.Count != 1 )
			return (false, "Source was mutated despite a MaxStack==1 split being structurally invalid.");

		return (true, null);
	}

	(bool, string) Test_S07_SplitDestinationOutOfBounds()
	{
		var container = NewContainer( 2 );
		var source = Stack( 5 );
		container.TryAdd( source, 0, 0 );
		var prepared = Stack( 2 );

		var result = InventoryStackTransactions.TrySplitAt( container, source.InstanceId, 2, prepared, container.Width, 0, false );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.DestinationInvalid || result.SpatialFailureReason != InventoryFailureReason.OutOfBounds )
			return (false, $"Expected DestinationInvalid/OutOfBounds, got Success={result.Success} Reason={result.FailureReason} Spatial={result.SpatialFailureReason}");

		if ( source.Quantity != 5 || container.Count != 1 )
			return (false, "Source was mutated despite an out-of-bounds destination.");

		return (true, null);
	}

	(bool, string) Test_S08_SplitDestinationOccupied()
	{
		var container = NewContainer();
		var source = Stack( 5 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( Stack( 1 ), 1, 0 );
		var prepared = Stack( 2 );

		var result = InventoryStackTransactions.TrySplitAt( container, source.InstanceId, 2, prepared, 1, 0, false );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.DestinationInvalid || result.SpatialFailureReason != InventoryFailureReason.Overlapping )
			return (false, $"Expected DestinationInvalid/Overlapping, got Success={result.Success} Reason={result.FailureReason} Spatial={result.SpatialFailureReason}");

		if ( source.Quantity != 5 || container.Count != 2 )
			return (false, "State changed despite an occupied destination.");

		return (true, null);
	}

	(bool, string) Test_S09_SplitRotationNotAllowed()
	{
		var container = NewContainer();
		var source = Stack( 5 );
		container.TryAdd( source, 0, 0 );
		var prepared = Stack( 2 );

		var result = InventoryStackTransactions.TrySplitAt( container, source.InstanceId, 2, prepared, 1, 0, targetRotated: true );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.DestinationInvalid || result.SpatialFailureReason != InventoryFailureReason.RotationNotAllowed )
			return (false, $"Expected DestinationInvalid/RotationNotAllowed, got Success={result.Success} Reason={result.FailureReason} Spatial={result.SpatialFailureReason}");

		if ( source.Quantity != 5 || container.Count != 1 )
			return (false, "State changed despite a disallowed rotation.");

		return (true, null);
	}

	(bool, string) Test_S10_SplitPreparedWrongDefinition()
	{
		var container = NewContainer();
		var source = Stack( 5 );
		container.TryAdd( source, 0, 0 );

		var result = InventoryStackTransactions.TrySplit( container, source.InstanceId, 2, Other( 2 ) );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.InvalidPreparedInstance )
			return (false, $"Expected InvalidPreparedInstance, got Success={result.Success} Reason={result.FailureReason}");

		if ( source.Quantity != 5 || container.Count != 1 )
			return (false, "State changed despite a prepared instance with the wrong definition.");

		return (true, null);
	}

	(bool, string) Test_S11_SplitPreparedWrongQuantity()
	{
		var container = NewContainer();
		var source = Stack( 5 );
		container.TryAdd( source, 0, 0 );

		var result = InventoryStackTransactions.TrySplit( container, source.InstanceId, 2, Stack( 3 ) );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.InvalidPreparedInstance )
			return (false, $"Expected InvalidPreparedInstance, got Success={result.Success} Reason={result.FailureReason}");

		if ( source.Quantity != 5 || container.Count != 1 )
			return (false, "State changed despite a prepared instance with a mismatched quantity.");

		return (true, null);
	}

	(bool, string) Test_S12_SplitPreparedInstanceIdAlreadyPresent()
	{
		var container = NewContainer();
		var source = Stack( 5 );
		container.TryAdd( source, 0, 0 );
		var existing = Stack( 1 );
		container.TryAdd( existing, 1, 0 );

		// Réutilise l'InstanceId d'un placement déjà présent comme instance "préparée" — doit être refusé.
		var duplicate = ItemInstance.Restore( existing.InstanceId, _stackable, 2 );
		var result = InventoryStackTransactions.TrySplit( container, source.InstanceId, 2, duplicate );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.PreparedInstanceAlreadyPresent )
			return (false, $"Expected PreparedInstanceAlreadyPresent, got Success={result.Success} Reason={result.FailureReason}");

		if ( source.Quantity != 5 || container.Count != 2 )
			return (false, "State changed despite a prepared instance whose InstanceId is already present.");

		return (true, null);
	}

	(bool, string) Test_S13_SplitSourceIdentityPreserved()
	{
		var container = NewContainer();
		var source = Stack( 5 );
		container.TryAdd( source, 0, 0 );
		var sourceId = source.InstanceId;

		var result = InventoryStackTransactions.TrySplit( container, source.InstanceId, 2, Stack( 2 ) );
		if ( !result.Success )
			return (false, $"TrySplit failed: {result.FailureReason}");

		if ( result.SourceInstanceId != sourceId || !container.Contains( sourceId ) )
			return (false, "Source InstanceId was not preserved by the split.");

		return (true, null);
	}

	(bool, string) Test_S14_SplitNewIdentityDistinct()
	{
		var container = NewContainer();
		var source = Stack( 5 );
		container.TryAdd( source, 0, 0 );

		var result = InventoryStackTransactions.TrySplit( container, source.InstanceId, 2, Stack( 2 ) );
		if ( !result.Success )
			return (false, $"TrySplit failed: {result.FailureReason}");

		if ( result.NewInstanceId is null || result.NewInstanceId == source.InstanceId )
			return (false, "Expected a distinct new InstanceId for the extracted stack.");

		return (true, null);
	}

	(bool, string) Test_S15_SplitWeightConserved()
	{
		var container = NewContainer();
		var source = Stack( 5 );
		container.TryAdd( source, 0, 0 );
		float before = container.CurrentWeight;

		var result = InventoryStackTransactions.TrySplit( container, source.InstanceId, 2, Stack( 2 ) );
		if ( !result.Success )
			return (false, $"TrySplit failed: {result.FailureReason}");

		if ( !NearlyEqual( container.CurrentWeight, before ) )
			return (false, $"Expected CurrentWeight to stay at {before}, got {container.CurrentWeight}");

		return (true, null);
	}

	(bool, string) Test_S16_SplitNoMutationOnFailure()
	{
		var container = NewContainer();
		var source = Stack( 5 );
		container.TryAdd( source, 0, 0 );

		InventoryStackTransactions.TrySplit( container, source.InstanceId, 0, Stack( 1 ) );
		InventoryStackTransactions.TrySplit( container, source.InstanceId, 5, Stack( 5 ) );
		InventoryStackTransactions.TrySplit( container, source.InstanceId, 2, Other( 2 ) );
		InventoryStackTransactions.TrySplitAt( container, source.InstanceId, 2, Stack( 2 ), container.Width, 0, false );

		if ( source.Quantity != 5 || container.Count != 1 )
			return (false, "One of the rejected split attempts left a partial mutation.");

		if ( !container.TryValidateState( out var error ) )
			return (false, $"TryValidateState failed after rejected splits: {error}");

		return (true, null);
	}

	// --- Merge exact ---

	(bool, string) Test_M01_MergePartialValid()
	{
		var container = NewContainer();
		var source = Stack( 4 );
		var target = Stack( 3 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );

		var result = InventoryStackTransactions.TryMergeExact( container, container, source.InstanceId, target.InstanceId, 2 );
		if ( !result.Success )
			return (false, $"TryMergeExact failed: {result.FailureReason}");

		if ( source.Quantity != 2 || target.Quantity != 5 )
			return (false, $"Expected source=2/target=5, got source={source.Quantity}/target={target.Quantity}");

		if ( !container.Contains( source.InstanceId ) || !container.Contains( target.InstanceId ) )
			return (false, "Both identities should survive a partial merge.");

		return (true, null);
	}

	(bool, string) Test_M02_MergeTotalValid()
	{
		var container = NewContainer();
		var source = Stack( 3 );
		var target = Stack( 4 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );

		var result = InventoryStackTransactions.TryMergeExact( container, container, source.InstanceId, target.InstanceId, 3 );
		if ( !result.Success )
			return (false, $"TryMergeExact failed: {result.FailureReason}");

		if ( target.Quantity != 7 )
			return (false, $"Expected target=7, got {target.Quantity}");

		if ( container.Contains( source.InstanceId ) )
			return (false, "Source InstanceId should be gone after a total merge.");

		return (true, null);
	}

	(bool, string) Test_M03_MergeSameSourceAndTarget()
	{
		var container = NewContainer();
		var item = Stack( 4 );
		container.TryAdd( item, 0, 0 );

		var result = InventoryStackTransactions.TryMergeExact( container, container, item.InstanceId, item.InstanceId, 1 );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.SameSourceAndTarget )
			return (false, $"Expected SameSourceAndTarget, got Success={result.Success} Reason={result.FailureReason}");

		if ( item.Quantity != 4 )
			return (false, "Quantity changed despite same source/target rejection.");

		return (true, null);
	}

	(bool, string) Test_M04_MergeIncompatibleStacks()
	{
		var container = NewContainer();
		var source = Stack( 4 );
		var target = Other( 4 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );

		var result = InventoryStackTransactions.TryMergeExact( container, container, source.InstanceId, target.InstanceId, 1 );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.IncompatibleStacks )
			return (false, $"Expected IncompatibleStacks, got Success={result.Success} Reason={result.FailureReason}");

		if ( source.Quantity != 4 || target.Quantity != 4 )
			return (false, "Quantities changed despite incompatible stacks.");

		return (true, null);
	}

	(bool, string) Test_M05_MergeTargetFull()
	{
		var container = NewContainer();
		var source = Stack( 4 );
		var target = Stack( 10 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );

		var result = InventoryStackTransactions.TryMergeExact( container, container, source.InstanceId, target.InstanceId, 1 );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.TargetFull )
			return (false, $"Expected TargetFull, got Success={result.Success} Reason={result.FailureReason}");

		if ( source.Quantity != 4 || target.Quantity != 10 )
			return (false, "Quantities changed despite a full target.");

		return (true, null);
	}

	(bool, string) Test_M06_MergeZeroAmount()
	{
		var container = NewContainer();
		var source = Stack( 4 );
		var target = Stack( 3 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );

		var result = InventoryStackTransactions.TryMergeExact( container, container, source.InstanceId, target.InstanceId, 0 );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.InvalidAmount )
			return (false, $"Expected InvalidAmount, got Success={result.Success} Reason={result.FailureReason}");

		return (true, null);
	}

	(bool, string) Test_M07_MergeAmountGreaterThanSource()
	{
		var container = NewContainer();
		var source = Stack( 4 );
		var target = Stack( 3 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );

		var result = InventoryStackTransactions.TryMergeExact( container, container, source.InstanceId, target.InstanceId, 5 );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.InsufficientSourceQuantity )
			return (false, $"Expected InsufficientSourceQuantity, got Success={result.Success} Reason={result.FailureReason}");

		return (true, null);
	}

	(bool, string) Test_M08_MergeInsufficientTargetCapacity()
	{
		var container = NewContainer();
		var source = Stack( 5 );
		var target = Stack( 8 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );

		var result = InventoryStackTransactions.TryMergeExact( container, container, source.InstanceId, target.InstanceId, 3 );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.InsufficientTargetCapacity )
			return (false, $"Expected InsufficientTargetCapacity, got Success={result.Success} Reason={result.FailureReason}");

		if ( source.Quantity != 5 || target.Quantity != 8 )
			return (false, "Quantities changed despite insufficient capacity.");

		return (true, null);
	}

	(bool, string) Test_M09_MergeSourceMissing()
	{
		var container = NewContainer();
		var target = Stack( 4 );
		container.TryAdd( target, 0, 0 );

		var result = InventoryStackTransactions.TryMergeExact( container, container, Guid.NewGuid(), target.InstanceId, 1 );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.SourceNotFound )
			return (false, $"Expected SourceNotFound, got Success={result.Success} Reason={result.FailureReason}");

		if ( target.Quantity != 4 || !container.Contains( target.InstanceId ) )
			return (false, "Target was mutated despite a missing source.");

		return (true, null);
	}

	(bool, string) Test_M10_MergeTargetMissing()
	{
		var container = NewContainer();
		var source = Stack( 4 );
		container.TryAdd( source, 0, 0 );

		var result = InventoryStackTransactions.TryMergeExact( container, container, source.InstanceId, Guid.NewGuid(), 1 );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.TargetNotFound )
			return (false, $"Expected TargetNotFound, got Success={result.Success} Reason={result.FailureReason}");

		if ( source.Quantity != 4 || !container.Contains( source.InstanceId ) )
			return (false, "Source was mutated despite a missing target.");

		return (true, null);
	}

	(bool, string) Test_M11_MergeTargetIdentityPreserved()
	{
		var container = NewContainer();
		var source = Stack( 3 );
		var target = Stack( 4 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );
		var targetId = target.InstanceId;

		var result = InventoryStackTransactions.TryMergeExact( container, container, source.InstanceId, targetId, 2 );
		if ( !result.Success )
			return (false, $"TryMergeExact failed: {result.FailureReason}");

		if ( result.TargetInstanceId != targetId || !container.Contains( targetId ) )
			return (false, "Target InstanceId was not preserved.");

		return (true, null);
	}

	(bool, string) Test_M12_MergePartialSourceIdentityPreserved()
	{
		var container = NewContainer();
		var source = Stack( 5 );
		var target = Stack( 1 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );
		var sourceId = source.InstanceId;

		var result = InventoryStackTransactions.TryMergeExact( container, container, sourceId, target.InstanceId, 2 );
		if ( !result.Success )
			return (false, $"TryMergeExact failed: {result.FailureReason}");

		if ( !container.Contains( sourceId ) || source.Quantity != 3 )
			return (false, "Source identity/quantity not preserved after a partial merge.");

		return (true, null);
	}

	(bool, string) Test_M13_MergeTotalSourceIdentityRemoved()
	{
		var container = NewContainer();
		var source = Stack( 3 );
		var target = Stack( 1 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );
		var sourceId = source.InstanceId;

		var result = InventoryStackTransactions.TryMergeExact( container, container, sourceId, target.InstanceId, 3 );
		if ( !result.Success )
			return (false, $"TryMergeExact failed: {result.FailureReason}");

		if ( container.Contains( sourceId ) )
			return (false, "Source InstanceId should have been removed after a total merge.");

		return (true, null);
	}

	(bool, string) Test_M14_MergeNoMutationOnFailure()
	{
		var container = NewContainer();
		var source = Stack( 4 );
		var target = Stack( 4 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );

		InventoryStackTransactions.TryMergeExact( container, container, source.InstanceId, target.InstanceId, 0 );
		InventoryStackTransactions.TryMergeExact( container, container, source.InstanceId, target.InstanceId, 5 );
		InventoryStackTransactions.TryMergeExact( container, container, source.InstanceId, Guid.NewGuid(), 1 );

		if ( source.Quantity != 4 || target.Quantity != 4 || container.Count != 2 )
			return (false, "One of the rejected merges left a partial mutation.");

		if ( !container.TryValidateState( out var error ) )
			return (false, $"TryValidateState failed after rejected merges: {error}");

		return (true, null);
	}

	(bool, string) Test_M15_MergeSourceNotStackable()
	{
		var container = NewContainer();
		var source = Single();
		var target = Stack( 4 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );

		var result = InventoryStackTransactions.TryMergeExact( container, container, source.InstanceId, target.InstanceId, 1 );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.SourceNotStackable )
			return (false, $"Expected SourceNotStackable, got Success={result.Success} Reason={result.FailureReason}");

		if ( result.MovedAmount != 0 )
			return (false, $"Expected MovedAmount=0, got {result.MovedAmount}");

		if ( source.Quantity != 1 || target.Quantity != 4 )
			return (false, "Quantities changed despite a non-stackable source being rejected.");

		if ( !container.Contains( source.InstanceId ) || !container.Contains( target.InstanceId ) )
			return (false, "Placements/InstanceId disappeared despite a rejected merge.");

		return (true, null);
	}

	(bool, string) Test_M16_MergeSameMissingIdReportsSourceNotFound()
	{
		var container = NewContainer();
		var missingId = Guid.NewGuid();

		var result = InventoryStackTransactions.TryMergeExact( container, container, missingId, missingId, 1 );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.SourceNotFound )
			return (false, $"Expected SourceNotFound (existence checked before same-id), got Success={result.Success} Reason={result.FailureReason}");

		return (true, null);
	}

	// --- Merge jusqu'à capacité ---

	(bool, string) Test_F01_SourceSmallerThanCapacity()
	{
		var container = NewContainer();
		var source = Stack( 2 );
		var target = Stack( 3 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );

		var result = InventoryStackTransactions.TryMergeUntilCapacity( container, container, source.InstanceId, target.InstanceId );
		if ( !result.Success )
			return (false, $"TryMergeUntilCapacity failed: {result.FailureReason}");

		if ( result.MovedAmount != 2 || target.Quantity != 5 || container.Contains( source.InstanceId ) )
			return (false, "Expected the whole source (2) to move into the target.");

		return (true, null);
	}

	(bool, string) Test_F02_SourceGreaterThanCapacity()
	{
		var container = NewContainer();
		var source = Stack( 8 );
		var target = Stack( 8 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );

		var result = InventoryStackTransactions.TryMergeUntilCapacity( container, container, source.InstanceId, target.InstanceId );
		if ( !result.Success )
			return (false, $"TryMergeUntilCapacity failed: {result.FailureReason}");

		if ( result.MovedAmount != 2 || target.Quantity != 10 || source.Quantity != 6 || !container.Contains( source.InstanceId ) )
			return (false, "Expected exactly 2 units moved, source retaining the remainder.");

		return (true, null);
	}

	(bool, string) Test_F03_MovedAmountExact()
	{
		var container = NewContainer();
		var source = Stack( 5 );
		var target = Stack( 9 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );

		var result = InventoryStackTransactions.TryMergeUntilCapacity( container, container, source.InstanceId, target.InstanceId );
		if ( !result.Success )
			return (false, $"TryMergeUntilCapacity failed: {result.FailureReason}");

		if ( result.MovedAmount != 1 )
			return (false, $"Expected MovedAmount=1, got {result.MovedAmount}");

		return (true, null);
	}

	(bool, string) Test_F04_TargetFull()
	{
		var container = NewContainer();
		var source = Stack( 3 );
		var target = Stack( 10 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );

		var result = InventoryStackTransactions.TryMergeUntilCapacity( container, container, source.InstanceId, target.InstanceId );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.TargetFull )
			return (false, $"Expected TargetFull, got Success={result.Success} Reason={result.FailureReason}");

		if ( source.Quantity != 3 || target.Quantity != 10 )
			return (false, "Quantities changed despite a full target.");

		return (true, null);
	}

	(bool, string) Test_F05_MergeTotal()
	{
		var container = NewContainer();
		var source = Stack( 2 );
		var target = Stack( 3 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );
		var sourceId = source.InstanceId;

		var result = InventoryStackTransactions.TryMergeUntilCapacity( container, container, sourceId, target.InstanceId );
		if ( !result.Success )
			return (false, $"TryMergeUntilCapacity failed: {result.FailureReason}");

		if ( container.Contains( sourceId ) )
			return (false, "Source InstanceId should be gone after a full merge-until-capacity.");

		return (true, null);
	}

	(bool, string) Test_F06_MergePartial()
	{
		var container = NewContainer();
		var source = Stack( 8 );
		var target = Stack( 8 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );
		var sourceId = source.InstanceId;

		var result = InventoryStackTransactions.TryMergeUntilCapacity( container, container, sourceId, target.InstanceId );
		if ( !result.Success )
			return (false, $"TryMergeUntilCapacity failed: {result.FailureReason}");

		if ( !container.Contains( sourceId ) || source.Quantity != 6 )
			return (false, "Source identity/quantity not preserved after a capped partial merge.");

		return (true, null);
	}

	(bool, string) Test_F07_UntilCapacitySuccessRequestedAmountIsZero()
	{
		var container = NewContainer();
		var source = Stack( 2 );
		var target = Stack( 3 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );

		var result = InventoryStackTransactions.TryMergeUntilCapacity( container, container, source.InstanceId, target.InstanceId );
		if ( !result.Success )
			return (false, $"TryMergeUntilCapacity failed: {result.FailureReason}");

		if ( result.RequestedAmount != 0 )
			return (false, $"Expected RequestedAmount=0 on success (TryMergeUntilCapacity has no explicit requested amount), got {result.RequestedAmount}");

		if ( result.MovedAmount != 2 )
			return (false, $"Expected MovedAmount=2, got {result.MovedAmount}");

		return (true, null);
	}

	(bool, string) Test_F08_UntilCapacityFailureRequestedAmountIsZero()
	{
		var container = NewContainer();
		var source = Stack( 3 );
		var target = Stack( 10 );
		container.TryAdd( source, 0, 0 );
		container.TryAdd( target, 1, 0 );

		var result = InventoryStackTransactions.TryMergeUntilCapacity( container, container, source.InstanceId, target.InstanceId );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.TargetFull )
			return (false, $"Expected TargetFull, got Success={result.Success} Reason={result.FailureReason}");

		if ( result.RequestedAmount != 0 )
			return (false, $"Expected RequestedAmount=0 on failure, got {result.RequestedAmount}");

		if ( result.MovedAmount != 0 )
			return (false, $"Expected MovedAmount=0 on failure, got {result.MovedAmount}");

		return (true, null);
	}

	// --- Transfert partiel vers une case vide ---

	(bool, string) Test_T01_TransferValidCrossContainer()
	{
		var sourceContainer = NewContainer();
		var targetContainer = NewContainer();
		var source = Stack( 5 );
		sourceContainer.TryAdd( source, 0, 0 );
		var prepared = Stack( 2 );

		var result = InventoryStackTransactions.TryTransferPartialToEmpty( sourceContainer, targetContainer, source.InstanceId, 2, prepared );
		if ( !result.Success )
			return (false, $"TryTransferPartialToEmpty failed: {result.FailureReason}");

		if ( source.Quantity != 3 || !sourceContainer.Contains( source.InstanceId ) )
			return (false, "Source should remain in the source container, decremented.");

		if ( !targetContainer.Contains( prepared.InstanceId ) || prepared.Quantity != 2 )
			return (false, "Prepared instance should be present in the target container.");

		return (true, null);
	}

	(bool, string) Test_T02_SplitViaTransferSameContainer()
	{
		var container = NewContainer();
		var source = Stack( 5 );
		container.TryAdd( source, 0, 0 );
		var prepared = Stack( 2 );

		var result = InventoryStackTransactions.TryTransferPartialToEmpty( container, container, source.InstanceId, 2, prepared );
		if ( !result.Success )
			return (false, $"TryTransferPartialToEmpty (same container) failed: {result.FailureReason}");

		if ( source.Quantity != 3 || !container.Contains( prepared.InstanceId ) )
			return (false, "Same-container transfer did not behave like a split.");

		return (true, null);
	}

	(bool, string) Test_T03_FullSourceAmountRejected()
	{
		var sourceContainer = NewContainer();
		var targetContainer = NewContainer();
		var source = Stack( 4 );
		sourceContainer.TryAdd( source, 0, 0 );

		var result = InventoryStackTransactions.TryTransferPartialToEmpty( sourceContainer, targetContainer, source.InstanceId, 4, Stack( 4 ) );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.InvalidAmount )
			return (false, $"Expected InvalidAmount, got Success={result.Success} Reason={result.FailureReason}");

		if ( source.Quantity != 4 || targetContainer.Count != 0 )
			return (false, "State changed despite a full-source-amount transfer being rejected.");

		return (true, null);
	}

	(bool, string) Test_T04_DestinationOccupied()
	{
		var sourceContainer = NewContainer();
		var targetContainer = NewContainer();
		var source = Stack( 4 );
		sourceContainer.TryAdd( source, 0, 0 );
		targetContainer.TryAdd( Stack( 1 ), 0, 0 );

		var result = InventoryStackTransactions.TryTransferPartialToEmptyAt( sourceContainer, targetContainer, source.InstanceId, 2, Stack( 2 ), 0, 0, false );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.DestinationInvalid || result.SpatialFailureReason != InventoryFailureReason.Overlapping )
			return (false, $"Expected DestinationInvalid/Overlapping, got Success={result.Success} Reason={result.FailureReason} Spatial={result.SpatialFailureReason}");

		if ( source.Quantity != 4 )
			return (false, "Source mutated despite an occupied destination.");

		return (true, null);
	}

	(bool, string) Test_T05_DestinationOutOfBounds()
	{
		var sourceContainer = NewContainer();
		var targetContainer = NewContainer( 2 );
		var source = Stack( 4 );
		sourceContainer.TryAdd( source, 0, 0 );

		var result = InventoryStackTransactions.TryTransferPartialToEmptyAt( sourceContainer, targetContainer, source.InstanceId, 2, Stack( 2 ), targetContainer.Width, 0, false );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.DestinationInvalid || result.SpatialFailureReason != InventoryFailureReason.OutOfBounds )
			return (false, $"Expected DestinationInvalid/OutOfBounds, got Success={result.Success} Reason={result.FailureReason} Spatial={result.SpatialFailureReason}");

		if ( source.Quantity != 4 )
			return (false, "Source mutated despite an out-of-bounds destination.");

		return (true, null);
	}

	(bool, string) Test_T06_PreparedInstanceInvalid()
	{
		var sourceContainer = NewContainer();
		var targetContainer = NewContainer();
		var source = Stack( 4 );
		sourceContainer.TryAdd( source, 0, 0 );

		var result = InventoryStackTransactions.TryTransferPartialToEmpty( sourceContainer, targetContainer, source.InstanceId, 2, Other( 2 ) );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.InvalidPreparedInstance )
			return (false, $"Expected InvalidPreparedInstance, got Success={result.Success} Reason={result.FailureReason}");

		if ( source.Quantity != 4 || targetContainer.Count != 0 )
			return (false, "State changed despite an invalid prepared instance.");

		return (true, null);
	}

	(bool, string) Test_T07_NewIdentityInTarget()
	{
		var sourceContainer = NewContainer();
		var targetContainer = NewContainer();
		var source = Stack( 4 );
		sourceContainer.TryAdd( source, 0, 0 );
		var prepared = Stack( 2 );

		var result = InventoryStackTransactions.TryTransferPartialToEmpty( sourceContainer, targetContainer, source.InstanceId, 2, prepared );
		if ( !result.Success )
			return (false, $"TryTransferPartialToEmpty failed: {result.FailureReason}");

		if ( result.NewInstanceId != prepared.InstanceId || !targetContainer.Contains( prepared.InstanceId ) )
			return (false, "Expected the prepared instance's new InstanceId in the target container.");

		return (true, null);
	}

	(bool, string) Test_T08_SourceIdentityPreserved()
	{
		var sourceContainer = NewContainer();
		var targetContainer = NewContainer();
		var source = Stack( 4 );
		sourceContainer.TryAdd( source, 0, 0 );
		var sourceId = source.InstanceId;

		var result = InventoryStackTransactions.TryTransferPartialToEmpty( sourceContainer, targetContainer, sourceId, 1, Stack( 1 ) );
		if ( !result.Success )
			return (false, $"TryTransferPartialToEmpty failed: {result.FailureReason}");

		if ( result.SourceInstanceId != sourceId || !sourceContainer.Contains( sourceId ) )
			return (false, "Source InstanceId was not preserved.");

		return (true, null);
	}

	(bool, string) Test_T09_WeightMovedExactly()
	{
		var sourceContainer = NewContainer();
		var targetContainer = NewContainer();
		var source = Stack( 4 );
		sourceContainer.TryAdd( source, 0, 0 );

		var result = InventoryStackTransactions.TryTransferPartialToEmpty( sourceContainer, targetContainer, source.InstanceId, 3, Stack( 3 ) );
		if ( !result.Success )
			return (false, $"TryTransferPartialToEmpty failed: {result.FailureReason}");

		float expected = _stackable.Weight * 3;
		if ( !NearlyEqual( targetContainer.CurrentWeight, expected ) )
			return (false, $"Expected target weight={expected}, got {targetContainer.CurrentWeight}");

		return (true, null);
	}

	(bool, string) Test_T10_TotalWeightConserved()
	{
		var sourceContainer = NewContainer();
		var targetContainer = NewContainer();
		var source = Stack( 4 );
		sourceContainer.TryAdd( source, 0, 0 );
		float totalBefore = sourceContainer.CurrentWeight + targetContainer.CurrentWeight;

		var result = InventoryStackTransactions.TryTransferPartialToEmpty( sourceContainer, targetContainer, source.InstanceId, 2, Stack( 2 ) );
		if ( !result.Success )
			return (false, $"TryTransferPartialToEmpty failed: {result.FailureReason}");

		float totalAfter = sourceContainer.CurrentWeight + targetContainer.CurrentWeight;
		if ( !NearlyEqual( totalBefore, totalAfter ) )
			return (false, $"Expected combined weight to stay at {totalBefore}, got {totalAfter}");

		return (true, null);
	}

	(bool, string) Test_T11_NoMutationOnFailure()
	{
		var sourceContainer = NewContainer();
		var targetContainer = NewContainer( 1 );
		var source = Stack( 4 );
		sourceContainer.TryAdd( source, 0, 0 );

		InventoryStackTransactions.TryTransferPartialToEmpty( sourceContainer, targetContainer, source.InstanceId, 4, Stack( 4 ) );
		InventoryStackTransactions.TryTransferPartialToEmpty( sourceContainer, targetContainer, source.InstanceId, 2, Other( 2 ) );
		targetContainer.TryAdd( Stack( 1 ), 0, 0 );
		InventoryStackTransactions.TryTransferPartialToEmptyAt( sourceContainer, targetContainer, source.InstanceId, 1, Stack( 1 ), 0, 0, false );

		if ( source.Quantity != 4 || !sourceContainer.Contains( source.InstanceId ) )
			return (false, "Source was mutated despite every attempted transfer being rejected.");

		if ( !sourceContainer.TryValidateState( out var sourceError ) )
			return (false, $"Source TryValidateState failed: {sourceError}");

		if ( !targetContainer.TryValidateState( out var targetError ) )
			return (false, $"Target TryValidateState failed: {targetError}");

		return (true, null);
	}

	(bool, string) Test_T12_TargetContainerNull()
	{
		var sourceContainer = NewContainer();
		var source = Stack( 4 );
		sourceContainer.TryAdd( source, 0, 0 );

		var result = InventoryStackTransactions.TryTransferPartialToEmpty( sourceContainer, null, source.InstanceId, 2, Stack( 2 ) );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.TargetNotFound )
			return (false, $"Expected TargetNotFound, got Success={result.Success} Reason={result.FailureReason}");

		if ( result.MovedAmount != 0 )
			return (false, $"Expected MovedAmount=0, got {result.MovedAmount}");

		if ( source.Quantity != 4 || !sourceContainer.Contains( source.InstanceId ) )
			return (false, "Source was mutated despite a null target container.");

		return (true, null);
	}

	// --- Merge entre deux conteneurs ---

	(bool, string) Test_C01_MergePartialCrossContainer()
	{
		var sourceContainer = NewContainer();
		var targetContainer = NewContainer();
		var source = Stack( 4 );
		var target = Stack( 3 );
		sourceContainer.TryAdd( source, 0, 0 );
		targetContainer.TryAdd( target, 0, 0 );

		var result = InventoryStackTransactions.TryMergeExact( sourceContainer, targetContainer, source.InstanceId, target.InstanceId, 2 );
		if ( !result.Success )
			return (false, $"TryMergeExact failed: {result.FailureReason}");

		if ( source.Quantity != 2 || target.Quantity != 5 )
			return (false, $"Expected source=2/target=5, got source={source.Quantity}/target={target.Quantity}");

		if ( !sourceContainer.Contains( source.InstanceId ) || !targetContainer.Contains( target.InstanceId ) )
			return (false, "Both identities should survive a partial cross-container merge.");

		return (true, null);
	}

	(bool, string) Test_C02_MergeTotalCrossContainer()
	{
		var sourceContainer = NewContainer();
		var targetContainer = NewContainer();
		var source = Stack( 3 );
		var target = Stack( 4 );
		sourceContainer.TryAdd( source, 0, 0 );
		targetContainer.TryAdd( target, 0, 0 );
		var sourceId = source.InstanceId;

		var result = InventoryStackTransactions.TryMergeExact( sourceContainer, targetContainer, sourceId, target.InstanceId, 3 );
		if ( !result.Success )
			return (false, $"TryMergeExact failed: {result.FailureReason}");

		if ( sourceContainer.Contains( sourceId ) )
			return (false, "Source InstanceId should be gone after a total cross-container merge.");

		if ( target.Quantity != 7 )
			return (false, $"Expected target=7, got {target.Quantity}");

		return (true, null);
	}

	(bool, string) Test_C03_TargetFull()
	{
		var sourceContainer = NewContainer();
		var targetContainer = NewContainer();
		var source = Stack( 3 );
		var target = Stack( 10 );
		sourceContainer.TryAdd( source, 0, 0 );
		targetContainer.TryAdd( target, 0, 0 );

		var result = InventoryStackTransactions.TryMergeExact( sourceContainer, targetContainer, source.InstanceId, target.InstanceId, 1 );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.TargetFull )
			return (false, $"Expected TargetFull, got Success={result.Success} Reason={result.FailureReason}");

		if ( source.Quantity != 3 || target.Quantity != 10 )
			return (false, "Quantities changed despite a full target.");

		return (true, null);
	}

	(bool, string) Test_C04_IncompatibleStacks()
	{
		var sourceContainer = NewContainer();
		var targetContainer = NewContainer();
		var source = Stack( 3 );
		var target = Other( 3 );
		sourceContainer.TryAdd( source, 0, 0 );
		targetContainer.TryAdd( target, 0, 0 );

		var result = InventoryStackTransactions.TryMergeExact( sourceContainer, targetContainer, source.InstanceId, target.InstanceId, 1 );
		if ( result.Success || result.FailureReason != StackTransactionFailureReason.IncompatibleStacks )
			return (false, $"Expected IncompatibleStacks, got Success={result.Success} Reason={result.FailureReason}");

		if ( source.Quantity != 3 || target.Quantity != 3 )
			return (false, "Quantities changed despite incompatible stacks.");

		if ( !sourceContainer.Contains( source.InstanceId ) || !targetContainer.Contains( target.InstanceId ) )
			return (false, "Placements/InstanceId disappeared despite a rejected merge.");

		return (true, null);
	}

	(bool, string) Test_C05_SourceRemovedOnlyOnFullMerge()
	{
		var partialSourceContainer = NewContainer();
		var partialTargetContainer = NewContainer();
		var partialSource = Stack( 4 );
		var partialTarget = Stack( 1 );
		partialSourceContainer.TryAdd( partialSource, 0, 0 );
		partialTargetContainer.TryAdd( partialTarget, 0, 0 );

		InventoryStackTransactions.TryMergeExact( partialSourceContainer, partialTargetContainer, partialSource.InstanceId, partialTarget.InstanceId, 2 );
		if ( !partialSourceContainer.Contains( partialSource.InstanceId ) )
			return (false, "Partial merge should not remove the source.");

		var totalSourceContainer = NewContainer();
		var totalTargetContainer = NewContainer();
		var totalSource = Stack( 2 );
		var totalTarget = Stack( 1 );
		totalSourceContainer.TryAdd( totalSource, 0, 0 );
		totalTargetContainer.TryAdd( totalTarget, 0, 0 );

		InventoryStackTransactions.TryMergeExact( totalSourceContainer, totalTargetContainer, totalSource.InstanceId, totalTarget.InstanceId, 2 );
		if ( totalSourceContainer.Contains( totalSource.InstanceId ) )
			return (false, "Total merge should remove the source.");

		return (true, null);
	}

	(bool, string) Test_C06_WeightMovedCorrectly()
	{
		var sourceContainer = NewContainer();
		var targetContainer = NewContainer();
		var source = Stack( 4 );
		var target = Stack( 1 );
		sourceContainer.TryAdd( source, 0, 0 );
		targetContainer.TryAdd( target, 0, 0 );

		var result = InventoryStackTransactions.TryMergeExact( sourceContainer, targetContainer, source.InstanceId, target.InstanceId, 3 );
		if ( !result.Success )
			return (false, $"TryMergeExact failed: {result.FailureReason}");

		float expectedTarget = _stackable.Weight * 4;
		if ( !NearlyEqual( targetContainer.CurrentWeight, expectedTarget ) )
			return (false, $"Expected target weight={expectedTarget}, got {targetContainer.CurrentWeight}");

		return (true, null);
	}

	(bool, string) Test_C07_NoDuplication()
	{
		var sourceContainer = NewContainer();
		var targetContainer = NewContainer();
		var source = Stack( 4 );
		var target = Stack( 3 );
		sourceContainer.TryAdd( source, 0, 0 );
		targetContainer.TryAdd( target, 0, 0 );
		int totalQuantityBefore = source.Quantity + target.Quantity;

		var result = InventoryStackTransactions.TryMergeExact( sourceContainer, targetContainer, source.InstanceId, target.InstanceId, 2 );
		if ( !result.Success )
			return (false, $"TryMergeExact failed: {result.FailureReason}");

		int totalQuantityAfter = ( sourceContainer.Contains( source.InstanceId ) ? source.Quantity : 0 ) + target.Quantity;
		if ( totalQuantityAfter != totalQuantityBefore )
			return (false, $"Expected combined quantity to stay at {totalQuantityBefore}, got {totalQuantityAfter}.");

		return (true, null);
	}

	(bool, string) Test_C08_NoLoss()
	{
		var sourceContainer = NewContainer();
		var targetContainer = NewContainer();
		var source = Stack( 3 );
		var target = Stack( 2 );
		sourceContainer.TryAdd( source, 0, 0 );
		targetContainer.TryAdd( target, 0, 0 );
		int totalQuantityBefore = source.Quantity + target.Quantity;

		var result = InventoryStackTransactions.TryMergeExact( sourceContainer, targetContainer, source.InstanceId, target.InstanceId, 3 );
		if ( !result.Success )
			return (false, $"TryMergeExact failed: {result.FailureReason}");

		if ( sourceContainer.Contains( source.InstanceId ) )
			return (false, "Expected a total merge (source fully absorbed).");

		if ( target.Quantity != totalQuantityBefore )
			return (false, $"Expected target to end with the combined quantity {totalQuantityBefore}, got {target.Quantity}.");

		return (true, null);
	}

	// --- Atomicité (séquence combinée — voir rapport de mission pour la limite honnête sur le
	// chemin de rollback de la seconde mutation, non provocable sans API dangereuse dédiée) ---

	(bool, string) Test_G01_ComboSequenceInvariants()
	{
		var containerA = NewContainer();
		var containerB = NewContainer();
		var itemA = Stack( 10 );
		containerA.TryAdd( itemA, 0, 0 );
		float totalWeightBefore = containerA.CurrentWeight + containerB.CurrentWeight;

		var split = InventoryStackTransactions.TrySplit( containerA, itemA.InstanceId, 4, Stack( 4 ) );
		if ( !split.Success )
			return (false, $"Setup split failed: {split.FailureReason}");

		var splitId = split.NewInstanceId!.Value;

		var transfer = InventoryStackTransactions.TryTransferPartialToEmpty( containerA, containerB, splitId, 1, Stack( 1 ) );
		if ( !transfer.Success )
			return (false, $"Setup transfer failed: {transfer.FailureReason}");

		var transferredId = transfer.NewInstanceId!.Value;

		var merge = InventoryStackTransactions.TryMergeExact( containerA, containerA, splitId, itemA.InstanceId, 3 );
		if ( !merge.Success )
			return (false, $"Setup merge failed: {merge.FailureReason}");

		foreach ( var placement in containerA.Placements.Concat( containerB.Placements ) )
		{
			if ( placement.Item.Quantity < 1 )
				return (false, $"Placement '{placement.InstanceId}' has a non-positive Quantity.");
		}

		if ( !containerA.TryValidateState( out var errorA ) )
			return (false, $"Container A TryValidateState failed: {errorA}");

		if ( !containerB.TryValidateState( out var errorB ) )
			return (false, $"Container B TryValidateState failed: {errorB}");

		float totalWeightAfter = containerA.CurrentWeight + containerB.CurrentWeight;
		if ( !NearlyEqual( totalWeightBefore, totalWeightAfter ) )
			return (false, $"Expected combined weight to stay at {totalWeightBefore}, got {totalWeightAfter}.");

		if ( !containerB.Contains( transferredId ) )
			return (false, "Transferred instance should remain in container B.");

		return (true, null);
	}
}
