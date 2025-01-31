﻿using System;
using System.Collections.Generic;
using System.Threading;
using Verse;
using SmashTools;

namespace Vehicles
{
	/// <summary>
	/// Region dirtyer handler for recaching
	/// </summary>
	public class VehicleRegionDirtyer
	{
		private readonly VehicleMapping mapping;
		private readonly VehicleDef createdFor;

		private readonly HashSet<IntVec3> dirtyCells = new HashSet<IntVec3>();

		[ThreadStatic]
		private static List<VehicleRegion> regionsToDirty;
		[ThreadStatic]
		private static List<VehicleRegion> regionsToDirtyFromWalkability = new List<VehicleRegion>();

		public static List<VehicleRegion> RegionsToDirty
		{
			get
			{
				if (regionsToDirty == null)
				{
					regionsToDirty = new List<VehicleRegion>();
				}
				return regionsToDirty;
			}
		}

		public static List<VehicleRegion> RegionsToDirtyFromWalkability
		{
			get
			{
				if (regionsToDirtyFromWalkability == null)
				{
					regionsToDirtyFromWalkability = new List<VehicleRegion>();
				}
				return regionsToDirtyFromWalkability;
			}
		}

		public VehicleRegionDirtyer(VehicleMapping mapping, VehicleDef createdFor)
		{
			this.mapping = mapping;
			this.createdFor = createdFor;
		}

		/// <summary>
		/// <see cref="dirtyCells"/> getter
		/// </summary>
		public HashSet<IntVec3> DirtyCells => dirtyCells;

		/// <summary>
		/// Any dirty cells registered
		/// </summary>
		public bool AnyDirty
		{
			get
			{
				return dirtyCells.Count > 0;
			}
		}

		/// <summary>
		/// Clear all dirtyed cells
		/// </summary>
		internal void SetAllClean()
		{
			dirtyCells.Clear();
		}

		/// <summary>
		/// Set all cells and regions to dirty status
		/// </summary>
		internal void SetAllDirty()
		{
			dirtyCells.Clear();
			foreach (IntVec3 cell in mapping.map)
			{
				dirtyCells.Add(cell);
			}
			foreach (VehicleRegion region in mapping[createdFor].VehicleRegionGrid.AllRegions_NoRebuild_InvalidAllowed)
			{
				SetRegionDirty(region, false);
			}
		}

		/// <summary>
		/// Notify that the walkable status at <paramref name="cell"/> has changed
		/// </summary>
		/// <remarks>Uses different static list, may be called from other threads than DedicatedThread for regions</remarks>
		/// <param name="cell"></param>
		public void Notify_WalkabilityChanged(IntVec3 cell)
		{
			RegionsToDirtyFromWalkability.Clear();
			for (int i = 0; i < 9; i++)
			{
				IntVec3 adjCell = cell + GenAdj.AdjacentCellsAndInside[i];
				if (adjCell.InBounds(mapping.map))
				{
					VehicleRegion regionAt_NoRebuild_InvalidAllowed = mapping[createdFor].VehicleRegionGrid.GetRegionAt_NoRebuild_InvalidAllowed(adjCell);
					if (regionAt_NoRebuild_InvalidAllowed != null && regionAt_NoRebuild_InvalidAllowed.valid)
					{
						SetRegionDirty(regionAt_NoRebuild_InvalidAllowed, true);
					}
				}
			}
			if (GenGridVehicles.Walkable(cell, createdFor, mapping.map))
			{
				dirtyCells.Add(cell);
			}
			RegionsToDirtyFromWalkability.Clear();
		}

		public void Notify_ThingAffectingRegionsSpawned(CellRect occupiedRect)
		{
			RegionsToDirty.Clear();
			foreach (IntVec3 cell in occupiedRect.ExpandedBy(1).ClipInsideMap(mapping.map))
			{
				VehicleRegion validRegionAt_NoRebuild = mapping[createdFor].VehicleRegionGrid.GetValidRegionAt_NoRebuild(cell);
				if (validRegionAt_NoRebuild != null)
				{
					RegionsToDirty.Add(validRegionAt_NoRebuild);
				}
			}
			for (int i = 0; i < RegionsToDirty.Count; i++)
			{
				SetRegionDirty(RegionsToDirty[i], true);
			}
			RegionsToDirty.Clear();
		}

		public void Notify_ThingAffectingRegionsDespawned(CellRect occupiedRect)
		{
			RegionsToDirty.Clear();
			//IntVec2 sizeWithPadding = thing.def.size + new IntVec2(createdFor.SizePadding * 2, createdFor.SizePadding * 2); //Doubled to account for opposite directions (N to S, E to W)
			foreach (IntVec3 cell in occupiedRect.ExpandedBy(createdFor.SizePadding).ClipInsideMap(mapping.map))
			{
				if (cell.InBounds(mapping.map))
				{
					//mapping.map.debugDrawer.FlashCell(cell, 0);
					VehicleRegion validRegionAt_NoRebuild2 = mapping[createdFor].VehicleRegionGrid.GetValidRegionAt_NoRebuild(cell);
					if (validRegionAt_NoRebuild2 != null)
					{
						RegionsToDirty.Add(validRegionAt_NoRebuild2);
					}
				}
			}
			for (int i = 0; i < RegionsToDirty.Count; i++)
			{
				SetRegionDirty(RegionsToDirty[i], true);
			}
			RegionsToDirty.Clear();
			dirtyCells.AddRange(occupiedRect);
		}

		/// <summary>
		/// Set <paramref name="region"/> to dirty status, marking it for update
		/// </summary>
		/// <param name="region"></param>
		/// <param name="addCellsToDirtyCells"></param>
		private void SetRegionDirty(VehicleRegion region, bool addCellsToDirtyCells = true)
		{
			try
			{
				if (!region.valid)
				{
					return;
				}
				region.valid = false;
				region.Room = null;
				for (int i = 0; i < region.links.Count; i++)
				{
					region.links[i].Deregister(region, createdFor);
				}
				region.links.Clear();
				region.weights.Clear();
				if (addCellsToDirtyCells)
				{
					foreach (IntVec3 intVec in region.Cells)
					{
						dirtyCells.Add(intVec);
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error($"Exception thrown in SetRegionDirty. Null: {region is null} Room: {region?.Room is null} links: {region?.links is null} weights: {region?.weights is null}");
				throw ex;
			}
		}
	}
}
