﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using RimWorld;
using Verse;
using Harmony;
using SPExtendedLibrary;
using RimShips.Defs;
using Verse.Sound;

namespace RimShips
{
    public class CompCannons : ThingComp
    {
        private float range;
        private List<SPExtended.SPTuple<Stack<int>, CannonHandler>> broadsideFire = new List<SPExtended.SPTuple<Stack<int>, CannonHandler>>();
        private List<CannonHandler> cannons = new List<CannonHandler>();
        public CompProperties_Cannons Props => (CompProperties_Cannons)this.props;
        public float MaxRange => this.cannons.Min(x => x.maxRange);
        public float MinRange => this.cannons.Max(x => x.minRange);
        public Pawn Pawn => parent as Pawn;
        public CompShips CompShip => this.Pawn.GetComp<CompShips>();

        public List<CannonHandler> Cannons
        {
            get
            {
                return this?.cannons ?? new List<CannonHandler>();
            }
        }

        public float Range
        {
            get
            {
                if (this.range <= 0) this.range = this.MaxRange;
                return this.range;
            }
            set
            {
                this.range = SPExtended.Clamp(value, this.MinRange, this.MaxRange);
            }
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            if (this?.cannons?.Count > 0 && this.Pawn.Drafted)
                GenDraw.DrawRadiusRing(this.Pawn.Position, this.Range);
        }
        
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (this.Pawn.Drafted)
            {
                if (this.cannons != null && this.cannons.Count > 0)
                {
                    if (this.cannons.Any(x => x.weaponType == WeaponType.Broadside))
                    {
                        if (this.cannons.Any(x => x.weaponLocation == WeaponLocation.Port))
                        {
                            CannonHandler cannon = this.cannons.Find(x => x.weaponLocation == WeaponLocation.Port);

                            Command_CooldownAction portSideCannons = new Command_CooldownAction();
                            portSideCannons.cannon = cannon;
                            portSideCannons.comp = this;
                            portSideCannons.defaultLabel = "CannonLabel".Translate(cannon.label);
                            portSideCannons.icon = TexCommandShips.BroadsideCannon_Port;
                            portSideCannons.action = delegate ()
                            {
                                SPExtended.SPTuple<Stack<int>, CannonHandler> tmpCannonItem = new SPExtended.SPTuple<Stack<int>, CannonHandler>(new Stack<int>(), cannon);
                                List<int> cannonOrder = Enumerable.Range(0, cannon.numberCannons).ToList();
                                cannonOrder.SPShuffle();
                                foreach (int i in cannonOrder)
                                {
                                    tmpCannonItem.First.Push(i);
                                }
                                this.broadsideFire.Add(tmpCannonItem);
                            };
                            portSideCannons.hotKey = KeyBindingDefOf.Misc4;
                            foreach (ShipHandler handler in this.CompShip.handlers)
                            {
                                if (handler.role.handlingType == HandlingTypeFlags.Cannons && handler.handlers.Count < handler.role.slotsToOperate)
                                {
                                    portSideCannons.Disable("NotEnoughCannonCrew".Translate(this.Pawn.LabelShort, handler.role.label));
                                }
                            }
                            yield return portSideCannons;
                        }
                        if (this.cannons.Any(x => x.weaponLocation == WeaponLocation.Starboard))
                        {
                            CannonHandler cannon = this.cannons.Find(x => x.weaponLocation == WeaponLocation.Starboard);

                            Command_CooldownAction starboardSideCannons = new Command_CooldownAction();
                            starboardSideCannons.cannon = cannon;
                            starboardSideCannons.comp = this;
                            starboardSideCannons.defaultLabel = "CannonLabel".Translate(cannon.label);
                            starboardSideCannons.icon = TexCommandShips.BroadsideCannon_Starboard;
                            starboardSideCannons.action = delegate ()
                            {
                                SPExtended.SPTuple<Stack<int>, CannonHandler> tmpCannonItem = new SPExtended.SPTuple<Stack<int>, CannonHandler>(new Stack<int>(), cannon);
                                List<int> cannonOrder = Enumerable.Range(0, cannon.numberCannons).ToList();
                                cannonOrder.SPShuffle();
                                foreach (int i in cannonOrder)
                                {
                                    tmpCannonItem.First.Push(i);
                                }
                                this.broadsideFire.Add(tmpCannonItem);
                            };
                            starboardSideCannons.hotKey = KeyBindingDefOf.Misc5;
                            foreach (ShipHandler handler in this.CompShip.handlers)
                            {
                                if (handler.role.handlingType == HandlingTypeFlags.Cannons && handler.handlers.Count < handler.role.slotsToOperate)
                                {
                                    starboardSideCannons.Disable("NotEnoughCannonCrew".Translate(this.Pawn.LabelShort, handler.role.label));
                                }
                            }
                            yield return starboardSideCannons;
                        }

                        Command_SetRange range = new Command_SetRange();
                        range.defaultLabel = "SetRange".Translate();
                        range.icon = TexCommandShips.UnloadCaptain;
                        range.activeCannons = this.cannons.FindAll(x => x.weaponType == WeaponType.Broadside);
                        range.cannonComp = this;
                        yield return range;
                    }
                }
            }
        }

        private void ResolveCannons()
        {
            if (!this.Pawn.Drafted && this.broadsideFire.Count > 0)
            {
                foreach(SPExtended.SPTuple<Stack<int>, CannonHandler> side in broadsideFire)
                {
                    side.Second.Reloading = true;
                }
                this.broadsideFire.Clear();
            }

            if (this.broadsideFire.Count > 0)
            {
                for (int i = 0; i < this.broadsideFire.Count; i++)
                {
                    SPExtended.SPTuple<Stack<int>, CannonHandler> side = broadsideFire[i];
                    side.Second.Reloading = false;

                    if (Find.TickManager.TicksGame % side.Second.TicksPerShot == 0)
                    {
                        this.FireCannonBroadside(side.Second, side.First.Pop());
                    }
                    if (!side.First.Any())
                    {
                        side.Second.Reloading = true;
                        broadsideFire.RemoveAt(i);
                    }
                }
            }
        }

        public void FireCannon(CannonHandler cannon)
        {
            if (cannon is null) return;

            float initialOffset = (cannon.spacing * (cannon.numberCannons - 1)) / 2f; // s(n-1) / 2
            float projectileOffset = (this.Pawn.def.size.x / 2f) + 1; // (s/2) + 1
            for (int i = 0; i < cannon.numberCannons; i++)
            {
                float offset = cannon.spacing * i - initialOffset; //s*i - x
                SPExtended.SPTuple<float, float> angleOffset = this.AngleRotationProjectileOffset(offset, projectileOffset);
                ThingDef projectile = cannon.projectile;
                IntVec3 targetCell = IntVec3.Invalid;
                Vector3 launchCell = this.Pawn.DrawPos;
                switch (cannon.weaponLocation)
                {
                    case WeaponLocation.Port:
                        if (this.CompShip.Angle == 0)
                        {
                            if (this.Pawn.Rotation == Rot4.North)
                            {
                                launchCell.x -= projectileOffset;
                                launchCell.z += offset;
                                targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                                targetCell.x -= (int)this.Range;
                            }
                            else if (this.Pawn.Rotation == Rot4.East)
                            {
                                launchCell.x += offset;
                                launchCell.z += projectileOffset;
                                targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                                targetCell.z += (int)this.Range;
                            }
                            else if (this.Pawn.Rotation == Rot4.South)
                            {
                                launchCell.x += projectileOffset;
                                launchCell.z += offset;
                                targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                                targetCell.x += (int)this.Range;
                            }
                            else if (this.Pawn.Rotation == Rot4.West)
                            {
                                launchCell.x += offset;
                                launchCell.z -= projectileOffset;
                                targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                                targetCell.z -= (int)this.Range;
                            }
                        }
                        else
                        {
                            if (this.Pawn.Rotation == Rot4.East && this.CompShip.Angle == -45)
                            {
                                targetCell = this.Pawn.Position;
                                targetCell.x -= (int)(Math.Cos(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                                targetCell.z -= (int)(Math.Sin(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                                launchCell.x += angleOffset.First;
                                launchCell.z += angleOffset.Second;
                            }
                            else if (this.Pawn.Rotation == Rot4.East && this.CompShip.Angle == 45)
                            {
                                targetCell = this.Pawn.Position;
                                targetCell.x += (int)(Math.Cos(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                                targetCell.z += (int)(Math.Sin(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                                launchCell.x += angleOffset.First;
                                launchCell.z += angleOffset.Second;
                            }
                            else if (this.Pawn.Rotation == Rot4.West && this.CompShip.Angle == -45)
                            {
                                targetCell = this.Pawn.Position;
                                targetCell.x += (int)(Math.Cos(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                                targetCell.z += (int)(Math.Sin(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                                launchCell.x -= angleOffset.First;
                                launchCell.z += angleOffset.Second;

                            }
                            else if (this.Pawn.Rotation == Rot4.West && this.CompShip.Angle == 45)
                            {
                                targetCell = this.Pawn.Position;
                                targetCell.x -= (int)(Math.Cos(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                                targetCell.z -= (int)(Math.Sin(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                                launchCell.x -= angleOffset.First;
                                launchCell.z += angleOffset.Second;
                            }
                        }
                        break;
                    case WeaponLocation.Starboard:
                        if (this.CompShip.Angle == 0)
                        {
                            if (this.Pawn.Rotation == Rot4.North)
                            {
                                targetCell = this.Pawn.Position;
                                targetCell.x += (int)this.Range;
                                launchCell.x += projectileOffset;
                                launchCell.z += offset;
                            }
                            else if (this.Pawn.Rotation == Rot4.East)
                            {
                                targetCell = this.Pawn.Position;
                                targetCell.z -= (int)this.Range;
                                launchCell.z -= projectileOffset;
                                launchCell.x += offset;
                            }
                            else if (this.Pawn.Rotation == Rot4.South)
                            {
                                targetCell = this.Pawn.Position;
                                targetCell.x -= (int)this.Range;
                                launchCell.x -= projectileOffset;
                                launchCell.z += offset;
                            }
                            else if (this.Pawn.Rotation == Rot4.West)
                            {
                                targetCell = this.Pawn.Position;
                                targetCell.z += (int)this.Range;
                                launchCell.z += projectileOffset;
                                launchCell.x += offset;
                            }
                        }
                        else
                        {
                            if (this.Pawn.Rotation == Rot4.East && this.CompShip.Angle == -45)
                            {
                                targetCell = this.Pawn.Position;
                                targetCell.x += (int)(Math.Cos(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                                targetCell.z += (int)(Math.Sin(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                                launchCell.x -= angleOffset.First;
                                launchCell.z -= angleOffset.Second;
                            }
                            else if (this.Pawn.Rotation == Rot4.East && this.CompShip.Angle == 45)
                            {
                                targetCell = this.Pawn.Position;
                                targetCell.x -= (int)(Math.Cos(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                                targetCell.z -= (int)(Math.Sin(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                                launchCell.x -= angleOffset.First;
                                launchCell.z -= angleOffset.Second;
                            }
                            else if (this.Pawn.Rotation == Rot4.West && this.CompShip.Angle == -45)
                            {
                                targetCell = this.Pawn.Position;
                                targetCell.x -= (int)(Math.Cos(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                                targetCell.z -= (int)(Math.Sin(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                                launchCell.x += angleOffset.First;
                                launchCell.z -= angleOffset.Second;
                            }
                            else if (this.Pawn.Rotation == Rot4.West && this.CompShip.Angle == 45)
                            {
                                targetCell = this.Pawn.Position;
                                targetCell.x += (int)(Math.Cos(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                                targetCell.z += (int)(Math.Sin(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                                launchCell.x += angleOffset.First;
                                launchCell.z -= angleOffset.Second;
                            }
                        }
                        break;
                    case WeaponLocation.Turret:
                        throw new NotImplementedException();
                }
                LocalTargetInfo target = new LocalTargetInfo(targetCell);
                ShootLine shootLine;
                bool flag = TryFindShootLineFromTo(this.Pawn.Position, target, out shootLine);

                //FIX FOR MULTIPLAYER
                IntVec3 c = target.Cell + GenRadial.RadialPattern[Rand.Range(0, GenRadial.NumCellsInRadius(cannon.spreadRadius * (this.Range / cannon.maxRange)))];
                Projectile projectile2 = (Projectile)GenSpawn.Spawn(projectile, this.Pawn.Position, this.Pawn.Map, WipeMode.Vanish);
                if (cannon.cannonSound is null) SoundDefOf_Ships.Explosion_PirateCannon.PlayOneShot(new TargetInfo(this.Pawn.Position, this.Pawn.Map, false));
                else { cannon.cannonSound.PlayOneShot(new TargetInfo(this.Pawn.Position, this.Pawn.Map, false)); }
                projectile2.Launch(this.Pawn, launchCell, c, target, cannon.hitFlags);
            }
        }

        public void FireCannonBroadside(CannonHandler cannon, int i)
        {
            if (cannon is null) return;

            float initialOffset = (cannon.spacing * (cannon.numberCannons - 1)) / 2f; // s(n-1) / 2
            float projectileOffset = (this.Pawn.def.size.x / 2f); // (s/2)

            float offset = cannon.spacing * i - initialOffset; //s*i - x
            SPExtended.SPTuple<float, float> angleOffset = this.AngleRotationProjectileOffset(offset, projectileOffset);
            ThingDef projectile = cannon.projectile;
            IntVec3 targetCell = IntVec3.Invalid;
            Vector3 launchCell = this.Pawn.DrawPos;
            switch (cannon.weaponLocation)
            {
                case WeaponLocation.Port:
                    if (this.CompShip.Angle == 0)
                    {
                        if (this.Pawn.Rotation == Rot4.North)
                        {
                            launchCell.x -= projectileOffset;
                            launchCell.z += offset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x -= (int)this.Range;
                        }
                        else if (this.Pawn.Rotation == Rot4.East)
                        {
                            launchCell.x += offset;
                            launchCell.z += projectileOffset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.z += (int)this.Range;
                        }
                        else if (this.Pawn.Rotation == Rot4.South)
                        {
                            launchCell.x += projectileOffset;
                            launchCell.z += offset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.x += (int)this.Range;
                        }
                        else if (this.Pawn.Rotation == Rot4.West)
                        {
                            launchCell.x += offset;
                            launchCell.z -= projectileOffset;
                            targetCell = new IntVec3((int)launchCell.x, this.Pawn.Position.y, (int)launchCell.z);
                            targetCell.z -= (int)this.Range;
                        }
                    }
                    else
                    {
                        if (this.Pawn.Rotation == Rot4.East && this.CompShip.Angle == -45)
                        {
                            targetCell = this.Pawn.Position;
                            targetCell.x -= (int)(Math.Cos(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z -= (int)(Math.Sin(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                            launchCell.x += angleOffset.First;
                            launchCell.z += angleOffset.Second;
                        }
                        else if (this.Pawn.Rotation == Rot4.East && this.CompShip.Angle == 45)
                        {
                            targetCell = this.Pawn.Position;
                            targetCell.x += (int)(Math.Cos(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z += (int)(Math.Sin(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                            launchCell.x += angleOffset.First;
                            launchCell.z += angleOffset.Second;
                        }
                        else if (this.Pawn.Rotation == Rot4.West && this.CompShip.Angle == -45)
                        {
                            targetCell = this.Pawn.Position;
                            targetCell.x += (int)(Math.Cos(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z += (int)(Math.Sin(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                            launchCell.x -= angleOffset.First;
                            launchCell.z += angleOffset.Second;

                        }
                        else if (this.Pawn.Rotation == Rot4.West && this.CompShip.Angle == 45)
                        {
                            targetCell = this.Pawn.Position;
                            targetCell.x -= (int)(Math.Cos(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z -= (int)(Math.Sin(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                            launchCell.x -= angleOffset.First;
                            launchCell.z += angleOffset.Second;
                        }
                    }
                    break;
                case WeaponLocation.Starboard:
                    if (this.CompShip.Angle == 0)
                    {
                        if (this.Pawn.Rotation == Rot4.North)
                        {
                            targetCell = this.Pawn.Position;
                            targetCell.x += (int)this.Range;
                            launchCell.x += projectileOffset;
                            launchCell.z += offset;
                        }
                        else if (this.Pawn.Rotation == Rot4.East)
                        {
                            targetCell = this.Pawn.Position;
                            targetCell.z -= (int)this.Range;
                            launchCell.z -= projectileOffset;
                            launchCell.x += offset;
                        }
                        else if (this.Pawn.Rotation == Rot4.South)
                        {
                            targetCell = this.Pawn.Position;
                            targetCell.x -= (int)this.Range;
                            launchCell.x -= projectileOffset;
                            launchCell.z += offset;
                        }
                        else if (this.Pawn.Rotation == Rot4.West)
                        {
                            targetCell = this.Pawn.Position;
                            targetCell.z += (int)this.Range;
                            launchCell.z += projectileOffset;
                            launchCell.x += offset;
                        }
                    }
                    else
                    {
                        if (this.Pawn.Rotation == Rot4.East && this.CompShip.Angle == -45)
                        {
                            targetCell = this.Pawn.Position;
                            targetCell.x += (int)(Math.Cos(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z += (int)(Math.Sin(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                            launchCell.x -= angleOffset.First;
                            launchCell.z -= angleOffset.Second;
                        }
                        else if (this.Pawn.Rotation == Rot4.East && this.CompShip.Angle == 45)
                        {
                            targetCell = this.Pawn.Position;
                            targetCell.x -= (int)(Math.Cos(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z -= (int)(Math.Sin(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                            launchCell.x -= angleOffset.First;
                            launchCell.z -= angleOffset.Second;
                        }
                        else if (this.Pawn.Rotation == Rot4.West && this.CompShip.Angle == -45)
                        {
                            targetCell = this.Pawn.Position;
                            targetCell.x -= (int)(Math.Cos(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z -= (int)(Math.Sin(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                            launchCell.x += angleOffset.First;
                            launchCell.z -= angleOffset.Second;
                        }
                        else if (this.Pawn.Rotation == Rot4.West && this.CompShip.Angle == 45)
                        {
                            targetCell = this.Pawn.Position;
                            targetCell.x += (int)(Math.Cos(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                            targetCell.z += (int)(Math.Sin(this.CompShip.Angle.DegreesToRadians()) * this.Range);
                            launchCell.x += angleOffset.First;
                            launchCell.z -= angleOffset.Second;
                        }
                    }
                    break;
                case WeaponLocation.Turret:
                    throw new NotImplementedException();
            }
            LocalTargetInfo target = new LocalTargetInfo(targetCell);
            ShootLine shootLine;
            bool flag = TryFindShootLineFromTo(this.Pawn.Position, target, out shootLine);

            //FIX FOR MULTIPLAYER
            IntVec3 c = target.Cell + GenRadial.RadialPattern[Rand.Range(0, GenRadial.NumCellsInRadius(cannon.spreadRadius * (this.Range / cannon.maxRange)))];
            Projectile projectile2 = (Projectile)GenSpawn.Spawn(projectile, this.Pawn.Position, this.Pawn.Map, WipeMode.Vanish);
            if (cannon.cannonSound is null) SoundDefOf_Ships.Explosion_PirateCannon.PlayOneShot(new TargetInfo(this.Pawn.Position, this.Pawn.Map, false));
            else { cannon.cannonSound.PlayOneShot(new TargetInfo(this.Pawn.Position, this.Pawn.Map, false)); }
            GenSpawn.Spawn(EffectsDefOf_Ships.Gas_Smoke_CannonSmall, new IntVec3((int)launchCell.x, (int)launchCell.y, (int)launchCell.z), this.Pawn.Map);
            projectile2.Launch(this.Pawn, launchCell, c, target, cannon.hitFlags);

        }

        private SPExtended.SPTuple<float, float> AngleRotationProjectileOffset(float preOffsetX, float preOffsetY)
        {
            SPExtended.SPTuple<float, float> offset = new SPExtended.SPTuple<float, float>(preOffsetX, preOffsetY);
            switch (this.Pawn.Rotation.AsInt)
            {
                case 1:
                    if (this.CompShip.Angle == -45)
                    {
                        SPExtended.SPTuple<float, float> newOffset = SPExtended.RotatePointCounterClockwise(preOffsetX, preOffsetY, 45f);
                        offset.First = newOffset.First;
                        offset.Second = newOffset.Second;
                    }
                    else if (this.CompShip.Angle == 45)
                    {
                        SPExtended.SPTuple<float, float> newOffset = SPExtended.RotatePointClockwise(preOffsetX, preOffsetY, 45f);
                        offset.First = newOffset.First;
                        offset.Second = newOffset.Second;
                    }
                    break;
                case 3:
                    if (this.CompShip.Angle == -45)
                    {
                        SPExtended.SPTuple<float, float> newOffset = SPExtended.RotatePointClockwise(preOffsetX, preOffsetY, 225f);
                        offset.First = newOffset.First;
                        offset.Second = newOffset.Second;
                    }
                    else if (this.CompShip.Angle == 45)
                    {
                        SPExtended.SPTuple<float, float> newOffset = SPExtended.RotatePointCounterClockwise(preOffsetX, preOffsetY, 225f);
                        offset.First = newOffset.First;
                        offset.Second = newOffset.Second;
                    }
                    break;
                default:
                    return offset;
            }
            return offset;
        }

        private bool TryFindShootLineFromTo(IntVec3 root, LocalTargetInfo targ, out ShootLine resultingLine)
        {
            resultingLine = new ShootLine(root, targ.Cell);
            return false;
        }

        public override void CompTick()
        {
            base.CompTick();
            this.ResolveCannons();
            foreach(CannonHandler cannon in this.cannons)
            {
                cannon.DoTick();
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            this.InitializeCannons();
            this.broadsideFire = new List<SPExtended.SPTuple<Stack<int>, CannonHandler>>();
        }

        private void InitializeCannons()
        {
            if(this.cannons is null) this.cannons = new List<CannonHandler>();
            if(this.cannons.Count <= 0 && this.Props.cannons.Any())
            {
                foreach(CannonHandler cannon in this.Props.cannons)
                {
                    this.cannons.Add(new CannonHandler(this.Pawn, cannon));
                }
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref broadsideFire, "broadsideFire", LookMode.Deep);
            Scribe_Collections.Look(ref cannons, "cannons", LookMode.Deep);
        }
    }
}
