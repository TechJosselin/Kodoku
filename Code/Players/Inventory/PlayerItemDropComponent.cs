using System;
using Kodoku.Items;
using Kodoku.Items.Inventory;
using Kodoku.Player;

namespace Kodoku.Player.Inventory;

/// <summary>
/// Dépose une pile complète depuis l'inventaire canonique du host vers le monde — symétrique de
/// <see cref="Kodoku.Items.Interaction.WorldItemPickupComponent"/>, mais porté par le pawn lui-même
/// (siège de <see cref="PlayerInventoryComponent"/> via <see cref="Inventory"/>) plutôt que par
/// l'objet-monde : la RPC cible donc directement le propre pawn de l'appelant, comme
/// <see cref="PlayerInventoryComponent.RequestSnapshot"/>, pas un <c>Connection</c> résolu depuis
/// un objet tiers. V1 volontairement limitée au drop d'une pile entière — pas de quantité choisie,
/// pas de split. Voir docs/architecture/ITEM_ARCHITECTURE.md.
///
/// <see cref="RequestDrop"/> (transport, <c>[Rpc.Host]</c>) reste séparée de
/// <see cref="TryDropAuthoritative"/> (transaction métier, non-RPC, prend une <see cref="Connection"/>
/// explicite) — même séparation que le pickup, pour un futur outil de test déterministe.
/// </summary>
public sealed class PlayerItemDropComponent : Component
{
	/// <summary>
	/// Rayon de l'anneau de positions candidates autour du joueur (voir <see cref="TryComputeDropTransform"/>) —
	/// distance depuis <c>EyePosition</c> pour chacune des <see cref="RingAngles"/>, avant toute
	/// revalidation par trace.
	/// </summary>
	[Group( "Configuration" )]
	[Property]
	public float DropDistance { get; set; } = 48f;

	/// <summary>
	/// Marge conservée avant un obstacle détecté par la trace horizontale. Si l'obstacle est plus
	/// proche que cette marge, la position candidate est rejetée (voir <see cref="TryComputeDropTransform"/>)
	/// plutôt que de placer l'objet dans le joueur ou à l'intérieur de l'obstacle.
	/// </summary>
	[Group( "Configuration" )]
	[Property]
	public float ObstacleMargin { get; set; } = 8f;

	float _maxRange = 150f;
	/// <summary>
	/// Portée maximale absolue pour toute position candidate (distance depuis <c>EyePosition</c>) —
	/// filet de sécurité indépendant de <see cref="DropDistance"/>, au cas où celle-ci serait mal
	/// configurée. Même ordre de grandeur que
	/// <see cref="Kodoku.Items.Interaction.WorldItemPickupComponent.MaxPickupDistance"/> (150), pour
	/// qu'un item déposé reste toujours à une distance qu'un pickup immédiat pourrait couvrir.
	/// </summary>
	[Group( "Configuration" )]
	[Property]
	public float MaxRange
	{
		get => _maxRange;
		set => _maxRange = MathF.Max( 1f, value );
	}

	/// <summary>
	/// Résolu sur le même GameObject que <see cref="PlayerInventoryComponent"/> (racine de
	/// <c>kodoku_player.prefab</c>) — accès direct au <see cref="PlayerInventoryComponent.Container"/>
	/// canonique, jamais via un snapshot côté client.
	/// </summary>
	[RequireComponent]
	public PlayerInventoryComponent Inventory { get; set; }

	/// <summary>
	/// Point d'entrée réseau unique du drop. <c>[Rpc.Host]</c>, cible le propre pawn de l'appelant
	/// (même schéma que <see cref="PlayerInventoryComponent.RequestSnapshot"/>) : la vérification
	/// d'ownership se fait directement par comparaison à <c>GameObject.Network.Owner</c>, pas
	/// via <see cref="KodokuPlayerComponent.FindByConnection"/> — un client ne peut donc jamais faire
	/// dropper un item par le pawn d'un autre joueur en ciblant son <see cref="PlayerItemDropComponent"/>
	/// (tous les pawns restent networkés et donc appelables par n'importe quel client, pas seulement
	/// le sien). Ne fait confiance à aucune autre donnée du client que l'<c>InstanceId</c> sélectionné
	/// dans son propre snapshot local — jamais une quantité, une position, une définition ou un prefab.
	/// </summary>
	[Rpc.Host]
	public void RequestDrop( string instanceId )
	{
		var caller = Rpc.Caller;

		if ( GameObject.Network.Owner != caller )
		{
			Log.Warning( $"[Drop][Fail] '{caller?.DisplayName}' a demandé un drop sur '{GameObject.Name}', dont il n'est pas le propriétaire — refusé." );
			return;
		}

		if ( !Guid.TryParse( instanceId, out var parsedId ) || parsedId == Guid.Empty )
		{
			Log.Warning( $"[Drop][Fail] '{caller?.DisplayName}': InstanceId '{instanceId}' invalide." );
			return;
		}

		var result = TryDropAuthoritative( caller, parsedId );

		if ( result.Success )
			Log.Info( $"[Drop][Success] '{caller?.DisplayName}' a déposé l'item {parsedId} depuis '{GameObject.Name}'." );
		else
			Log.Warning( $"[Drop][Fail] '{caller?.DisplayName}' -> '{GameObject.Name}': {result.FailureReason}" );
	}

	/// <summary>
	/// Transaction métier complète, indépendante de tout transport réseau — même esprit que
	/// <see cref="Kodoku.Items.Interaction.WorldItemPickupComponent.TryPickupAuthoritative"/>.
	///
	/// Ordre strict, délibérément différent d'un simple « retirer puis spawner » : le clone du
	/// prefab monde reçoit l'<see cref="ItemInstance"/> existante (<see cref="WorldItemComponent.TryInitializeFromInstance"/>)
	/// *avant* le retrait canonique et *avant* <c>NetworkSpawn()</c>. Ainsi, si
	/// <see cref="WorldItemComponent.OnStart"/> s'exécute à un moment quelconque de son cycle de vie
	/// différé après le spawn, <see cref="WorldItemComponent.IsInitialized"/> est déjà vrai et son
	/// chemin de création d'une *nouvelle* instance ne peut structurellement jamais s'exécuter — ce
	/// n'est pas une garantie de timing, c'est une garantie d'état. La publication réseau
	/// (<see cref="WorldItemComponent.PublishAuthoritativeNetworkState"/>) reste en revanche après
	/// <c>NetworkSpawn()</c>, seul ordre validé par test réel dans ce projet (voir
	/// <see cref="Kodoku.Items.LootSpawnPointComponent"/>, Tests A à G) — aucun précédent
	/// confirmé qu'un <c>[Sync]</c> défini avant le spawn soit inclus dans l'état initial transmis.
	/// </summary>
	public DropResult TryDropAuthoritative( Connection requester, Guid instanceId )
	{
		// Garde défensive — ne devrait jamais se produire pour un appel host réel, mais on ne mute
		// jamais l'état de gameplay sans avoir vérifié l'autorité, même ici (même patron que
		// WorldItemPickupComponent.TryPickupAuthoritative).
		if ( !Networking.IsHost )
		{
			Log.Error( $"[Drop][InternalError] TryDropAuthoritative() exécuté hors du host pour '{GameObject.Name}'." );
			return DropResult.Fail( DropFailureReason.InternalError );
		}

		if ( instanceId == Guid.Empty )
			return DropResult.Fail( DropFailureReason.InvalidInstanceId );

		// 1. Résolution — l'appelant correspond-il à un pawn valide, et est-ce bien CE pawn ?
		var pawn = KodokuPlayerComponent.FindByConnection( Scene, requester );
		if ( pawn is null || pawn.PlayerController is null )
			return DropResult.Fail( DropFailureReason.InvalidCaller );

		// Défense en profondeur : le pawn résolu depuis `requester` doit être exactement celui qui
		// porte ce composant — la RPC vérifie déjà GameObject.Network.Owner == Rpc.Caller, mais un
		// futur appelant direct de cette méthode (test déterministe) doit retrouver la même garantie.
		if ( pawn.GameObject != GameObject )
			return DropResult.Fail( DropFailureReason.OwnershipRejected );

		if ( Inventory is null || Inventory.Container is null )
			return DropResult.Fail( DropFailureReason.InvalidCaller );

		var placement = Inventory.Container.GetPlacement( instanceId );
		if ( placement is null )
			return DropResult.Fail( DropFailureReason.ItemNotFound );

		var item = placement.Item;
		var definition = item.Definition;
		if ( definition is null || string.IsNullOrEmpty( definition.ItemId ) )
			return DropResult.Fail( DropFailureReason.InvalidDefinition );

		if ( item.Quantity < 1 )
			return DropResult.Fail( DropFailureReason.InvalidQuantity );

		if ( definition.WorldPrefabOverride is null )
			return DropResult.Fail( DropFailureReason.MissingWorldPrefab );

		if ( !TryComputeDropTransform( pawn, out var dropTransform ) )
			return DropResult.Fail( DropFailureReason.InvalidDropPosition );

		int originalX = placement.X;
		int originalY = placement.Y;
		bool originalRotated = placement.IsRotated;

		// 2. Préparation du GameObject monde — clone local, pas encore exposé sur le réseau.
		var spawned = Sandbox.GameObject.Clone( definition.WorldPrefabOverride, dropTransform );
		if ( spawned is null )
			return DropResult.Fail( DropFailureReason.CloneFailed );

		var worldItem = spawned.Components.Get<WorldItemComponent>();
		if ( worldItem is null )
		{
			spawned.Destroy();
			return DropResult.Fail( DropFailureReason.MissingWorldItemComponent );
		}

		// 3. Le clone reçoit l'ItemInstance existante avant tout retrait ou exposition réseau —
		// voir le commentaire de méthode ci-dessus pour la raison de cet ordre.
		if ( !worldItem.TryInitializeFromInstance( item ) )
		{
			spawned.Destroy();
			return DropResult.Fail( DropFailureReason.WorldInitializationFailed );
		}

		// 4. Retrait canonique — seulement une fois le clone prêt à recevoir cette instance.
		var removeResult = Inventory.Container.TryRemove( instanceId, out var removedItem );
		if ( !removeResult.Success || removedItem != item )
		{
			// Le clone n'est pas encore networké (pas de NetworkSpawn() appelé) : Destroy() reste
			// purement local, l'inventaire n'a subi aucune mutation observable.
			spawned.Destroy();
			return DropResult.Fail( DropFailureReason.ItemNotFound );
		}

		// 5. Exposition réseau — seulement une fois l'identité déjà fixée localement à l'étape 3.
		if ( !spawned.NetworkSpawn() )
		{
			bool rolledBack = TryRollback( item, originalX, originalY, originalRotated, instanceId, "NetworkSpawn() a échoué" );
			spawned.Destroy();
			return DropResult.Fail( rolledBack ? DropFailureReason.NetworkSpawnFailed : DropFailureReason.RollbackFailed );
		}

		if ( !worldItem.PublishAuthoritativeNetworkState() )
		{
			bool rolledBack = TryRollback( item, originalX, originalY, originalRotated, instanceId, "PublishAuthoritativeNetworkState() a échoué" );
			// Networké depuis l'étape précédente : la destruction se propage à tous les clients,
			// même patron que WorldItemPickupComponent après un pickup confirmé.
			spawned.Destroy();
			return DropResult.Fail( rolledBack ? DropFailureReason.WorldInitializationFailed : DropFailureReason.RollbackFailed );
		}

		// 6. Succès confirmé.
		Inventory.NotifyMutated();
		return DropResult.Ok();
	}

	/// <summary>
	/// Réinsère <paramref name="item"/> exactement à son placement d'origine — devrait toujours
	/// réussir, la transaction host étant synchrone et la cellule venant tout juste d'être libérée
	/// (voir mission). Ne touche jamais <see cref="PlayerInventoryComponent.NotifyMutated"/> : l'état
	/// canonique final est identique à avant la tentative, aucune nouvelle révision n'est due.
	/// </summary>
	bool TryRollback( ItemInstance item, int x, int y, bool rotated, Guid instanceId, string reason )
	{
		var reinsert = Inventory.Container.TryAdd( item, x, y, rotated );
		if ( !reinsert.Success )
		{
			Log.Error( $"[Drop][CriticalRollbackFailure] instance={instanceId} n'a pas pu être réinsérée à ({x},{y}) rotated={rotated} après « {reason} » : {reinsert.FailureReason}. " +
				$"Item perdu de l'inventaire canonique du pawn '{GameObject.Name}' — aucune nouvelle instance n'est créée pour compenser, une intervention manuelle peut être nécessaire." );
			return false;
		}

		Log.Warning( $"[Drop][Rollback] instance={instanceId} restaurée à ({x},{y}) rotated={rotated} sur '{GameObject.Name}' après « {reason} »." );
		return true;
	}

	/// <summary>
	/// Angles (degrés, autour de l'axe vertical, relatifs au regard du joueur) des positions
	/// candidates de <see cref="TryComputeDropTransform"/> — ordre de priorité strict, du plus proche
	/// du regard au plus éloigné, la position directement derrière le joueur (180°) en tout dernier
	/// recours. <strong>Choix explicite du 2026-07-15</strong> : la V1 initiale de cette recherche
	/// restait strictement devant le joueur (jamais 360°, jamais derrière) ; après un test réel où le
	/// devant du joueur était bloqué sur toute sa largeur (plusieurs `InvalidDropPosition` consécutifs
	/// avant qu'une rotation du joueur ne libère une candidate), ce choix a été explicitement révisé
	/// pour couvrir tout le tour du joueur — un item peut désormais apparaître derrière lui si c'est la
	/// seule place libre. Reste une liste fixe et bornée (8 candidates), pas une recherche continue.
	/// </summary>
	static readonly float[] RingAngles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };

	/// <summary>
	/// Position de drop calculée exclusivement côté host, jamais fournie par le client. Essaie une
	/// petite liste déterministe de positions candidates (voir <see cref="RingAngles"/>) réparties en
	/// anneau autour du joueur, toutes à <see cref="DropDistance"/> — et retient la <em>première</em>
	/// qui passe la validation par trace, dans l'ordre de <see cref="RingAngles"/> (le regard du joueur
	/// d'abord, puis progressivement vers les côtés, l'arrière en dernier recours — pour limiter les
	/// placements surprenants même en 360°).
	///
	/// Chaque candidate est validée indépendamment par <see cref="TryValidateCandidate"/> — même
	/// logique de trace pour chacune (jamais de raccourci qui traverserait un mur pour atteindre une
	/// position candidate, puisque la trace part toujours de <see cref="PlayerController.EyePosition"/>
	/// jusqu'à la candidate elle-même, pas d'une candidate à une autre). Refuse (<c>false</c>)
	/// uniquement si **aucune** des huit candidates n'est valide. Direction de chaque candidate obtenue
	/// en tournant la composante horizontale du regard (<c>EyeAngles.Forward</c>, aplatie sur le plan
	/// horizontal — un anneau de drop reste au niveau du joueur, pas incliné selon qu'il regarde en
	/// haut ou en bas) via <see cref="Rotation.FromYaw(float)"/>, confirmée par le XML des assemblies
	/// moteur locales — recherche bornée et déterministe, coût au plus huit candidates (jusqu'à trois
	/// traces courtes chacune), jamais un système général de placement/navigation.
	/// </summary>
	bool TryComputeDropTransform( KodokuPlayerComponent pawn, out Transform transform )
	{
		transform = default;

		var controller = pawn.PlayerController;
		if ( controller is null )
			return false;

		var origin = controller.EyePosition;
		var forward = controller.EyeAngles.Forward;
		var horizontalForward = new Vector3( forward.x, forward.y, 0f ).Normal;

		Span<Vector3> candidates = stackalloc Vector3[RingAngles.Length];
		for ( int i = 0; i < RingAngles.Length; i++ )
		{
			var direction = Rotation.FromYaw( RingAngles[i] ) * horizontalForward;
			candidates[i] = origin + direction * DropDistance;
		}

		foreach ( var candidate in candidates )
		{
			if ( TryValidateCandidate( pawn, origin, candidate, out transform ) )
				return true;
		}

		return false;
	}

	/// <summary>
	/// Rayon volumique partagé par les trois traces de <see cref="TryValidateCandidate"/> (horizontale,
	/// verticale, volume final) — une seule source de vérité. Reproduit la tolérance déjà utilisée par
	/// la revalidation de pickup — adapté au prototype actuel (un seul type d'item, encombrement
	/// réduit), pas encore une validation générique fondée sur les dimensions réelles de chaque
	/// <c>WorldPrefabOverride</c>. Un futur item volumineux pourrait nécessiter une trace dimensionnée
	/// sur son propre gabarit plutôt que ce rayon fixe — non construit ici, hors périmètre de cette V1.
	/// </summary>
	const float DropTraceRadius = 4f;

	/// <summary>
	/// Valide une unique position candidate, dans l'ordre suivant :
	///
	/// 1. distance depuis <paramref name="origin"/> &lt;= <see cref="MaxRange"/> (portée maximale
	///    raisonnable, filet de sécurité indépendant des autres propriétés) ;
	/// 2. trace horizontale du joueur vers la candidate — un obstacle plus proche que
	///    <see cref="ObstacleMargin"/> rejette cette candidate (l'appelant essaie la suivante) plutôt
	///    que de placer l'objet dans le joueur ou dans l'obstacle ; sinon le point retenu recule
	///    d'<see cref="ObstacleMargin"/> le long du rayon lui-même, ou conserve
	///    <paramref name="desiredPoint"/> si rien n'est touché ;
	/// 3. trace verticale courte (gestion du sol, inchangée) — évite un spawn sous le sol si un sol
	///    existe juste en dessous ; sans sol proche, le point horizontal est conservé tel quel (l'objet,
	///    muni d'un <c>Rigidbody</c> sur <c>water_bottle.prefab</c>, retombe naturellement) ;
	/// 4. la position finale ne doit pas entrer dans la capsule du joueur — <c>IgnoreGameObjectHierarchy</c>
	///    exclut délibérément le pawn des traces ci-dessus (même choix que la revalidation de portée du
	///    pickup), donc rien d'autre ne rejetterait une candidate trop proche de son propre corps (ex.
	///    un recul par marge d'obstacle qui ramènerait une candidate proche vers le joueur si
	///    <see cref="ObstacleMargin"/> est configurée plus petite que le rayon de capsule) — vérifié en
	///    horizontal seulement (<c>WithZ(0)</c>), le rayon de capsule du moteur étant une mesure
	///    horizontale ;
	/// 5. validation du volume à la position finale (<c>SceneTraceResult.StartedSolid</c>) — trace
	///    volumique locale <em>dédiée</em>, distincte de la trace horizontale du point 2 : elle part de
	///    <c>finalPosition</c> elle-même (après recul d'obstacle <em>et</em> ajustement au sol), jamais
	///    de <c>origin</c> — lire <c>StartedSolid</c> sur la trace horizontale n'aurait renseigné que sur
	///    l'œil du joueur, pas sur la position réellement retenue. Détecte un mur, le sol, ou tout autre
	///    collider chevauchant réellement ce volume à cet endroit précis ; ne considère jamais le clone
	///    du prefab monde (il n'existe pas encore à ce stade — le clone n'est créé qu'après un appel
	///    réussi à cette méthode, dans <see cref="TryDropAuthoritative"/>) ; ignore le pawn
	///    (<c>IgnoreGameObjectHierarchy</c>, déjà couvert séparément par le point 4 ci-dessus).
	///
	/// Pas de contrôle de composante <c>forward</c> ici — depuis le choix du 2026-07-15 (voir
	/// <see cref="RingAngles"/>), une candidate derrière le joueur est explicitement autorisée.
	/// </summary>
	bool TryValidateCandidate( KodokuPlayerComponent pawn, Vector3 origin, Vector3 desiredPoint, out Transform transform )
	{
		transform = default;

		var offset = desiredPoint - origin;

		if ( offset.Length > MaxRange )
			return false;

		var rayDirection = offset.Normal;

		// Trace horizontale du joueur vers la candidate — un obstacle plus proche que
		// ObstacleMargin rejette cette candidate (l'appelant essaie la suivante) plutôt que de
		// placer l'objet dans le joueur ou dans l'obstacle.
		var horizontal = Scene.Trace
			.Ray( origin, desiredPoint )
			.IgnoreGameObjectHierarchy( pawn.GameObject )
			.Radius( DropTraceRadius )
			.Run();

		if ( horizontal.Hit && horizontal.Distance <= ObstacleMargin )
			return false;

		var dropPoint = horizontal.Hit ? horizontal.EndPosition - rayDirection * ObstacleMargin : desiredPoint;

		// Trace verticale courte — gestion du sol (inchangée) : évite un spawn sous le sol si un
		// sol existe juste en dessous ; sans sol proche, dropPoint est conservé tel quel.
		var vertical = Scene.Trace
			.Ray( dropPoint, dropPoint + Vector3.Down * 64f )
			.IgnoreGameObjectHierarchy( pawn.GameObject )
			.Radius( DropTraceRadius )
			.Run();

		// finalPosition = position réellement retenue, après recul par rapport à l'obstacle
		// (dropPoint) ET après ajustement au sol (vertical) — les deux contrôles suivants portent
		// sur CE point précis, pas sur desiredPoint ni sur une trace intermédiaire.
		var finalPosition = vertical.Hit ? vertical.EndPosition + Vector3.Up * 2f : dropPoint;

		// Capsule du joueur : les traces ci-dessus ignorent délibérément pawn.GameObject
		// (IgnoreGameObjectHierarchy), donc aucune d'elles ne peut détecter que finalPosition est
		// trop proche du corps du joueur lui-même — vérifié séparément ici, en horizontal
		// uniquement (le rayon de capsule du moteur est une mesure horizontale).
		var horizontalDistanceToPlayer = (finalPosition - pawn.GameObject.WorldPosition).WithZ( 0f ).Length;
		if ( horizontalDistanceToPlayer < pawn.PlayerController.BodyRadius + DropTraceRadius )
			return false;

		// Validation du volume à finalPosition — trace volumique locale dédiée et très courte
		// (départ ET arrivée quasi confondus, 0.01 unité d'écart, seulement pour que Run() ait un
		// segment non nul à évaluer), PAS une relecture de horizontal.StartedSolid : horizontal
		// part de l'œil du joueur (origin), son StartedSolid ne renseignerait que sur l'origine de
		// CETTE trace-là, jamais sur finalPosition. IgnoreGameObjectHierarchy exclut le pawn (déjà
		// couvert séparément ci-dessus par BodyRadius) ; le clone du prefab monde n'existe pas
		// encore à ce stade (TryComputeDropTransform est appelée avant tout GameObject.Clone(...)
		// dans TryDropAuthoritative), donc rien à exclure de son côté. StartedSolid == true signifie
		// que la sphère de rayon DropTraceRadius posée à finalPosition chevauche déjà un mur, le
		// sol, ou tout autre collider présent à cet endroit précis.
		var overlap = Scene.Trace
			.Ray( finalPosition, finalPosition + Vector3.Up * 0.01f )
			.IgnoreGameObjectHierarchy( pawn.GameObject )
			.Radius( DropTraceRadius )
			.Run();

		if ( overlap.StartedSolid )
			return false;

		transform = new Transform( finalPosition, Rotation.Identity );
		return true;
	}
}
