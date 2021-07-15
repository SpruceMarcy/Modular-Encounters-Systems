using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Weapons;
using SpaceEngineers.Game.ModAPI;
using ProtoBuf;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Utils;
using VRageMath;
using ModularEncountersSystems;
using ModularEncountersSystems.Behavior;
using ModularEncountersSystems.Behavior.Subsystems;
using ModularEncountersSystems.Helpers;
using ModularEncountersSystems.Entities;
using ModularEncountersSystems.Behavior.Subsystems.Profiles;
using ModularEncountersSystems.Behavior.Subsystems.AutoPilot;
using ModularEncountersSystems.Core;
using ModularEncountersSystems.Logging;

namespace ModularEncountersSystems.Behavior {

    public class Strike : IBehaviorSubClass {

        //Configurable

        public double StrikeBeginSpaceAttackRunDistance;
        public double StrikeBeginPlanetAttackRunDistance;
        public double StrikeBreakawayDistance;
        public int StrikeOffsetRecalculationTime;
        public bool StrikeEngageUseSafePlanetPathing;
        public bool StrikeEngageUseCollisionEvasionSpace;
        public bool StrikeEngageUseCollisionEvasionPlanet;

        public bool EngageOverrideWithDistanceAndTimer;
        public int EngageOverrideTimerTrigger;
        public double EngageOverrideDistance;

        private bool _defaultCollisionSettings = false;

        public DateTime LastOffsetCalculation;
        public DateTime EngageOverrideTimer;
        public bool TargetIsHigh;

        public byte Counter;

        private IBehavior _behavior;

        public Strike(IBehavior behavior) {

            _behavior = behavior;

            StrikeBeginSpaceAttackRunDistance = 75;
            StrikeBeginPlanetAttackRunDistance = 100;
            StrikeBreakawayDistance = 450;
            StrikeOffsetRecalculationTime = 30;
            StrikeEngageUseSafePlanetPathing = true;
            StrikeEngageUseCollisionEvasionSpace = true;
            StrikeEngageUseCollisionEvasionPlanet = false;

            EngageOverrideWithDistanceAndTimer = true;
            EngageOverrideTimerTrigger = 20;
            EngageOverrideDistance = 1200;

            LastOffsetCalculation = MyAPIGateway.Session.GameDateTime;
            EngageOverrideTimer = MyAPIGateway.Session.GameDateTime;

            Counter = 0;

        }

        public void ProcessBehavior() {

            if (MES_SessionCore.IsServer == false) {

                return;

            }

            bool skipEngageCheck = false;

            if (_behavior.Mode != BehaviorMode.Retreat && _behavior.Settings.DoRetreat == true) {

                ChangeCoreBehaviorMode(BehaviorMode.Retreat);
                _behavior.AutoPilot.ActivateAutoPilot(_behavior.RemoteControl.GetPosition(), NewAutoPilotMode.RotateToWaypoint | NewAutoPilotMode.ThrustForward | NewAutoPilotMode.PlanetaryPathing, CheckEnum.Yes, CheckEnum.No);

            }

            //Init
            if (_behavior.Mode == BehaviorMode.Init) {

                if (!_behavior.AutoPilot.Targeting.HasTarget()) {

                    ChangeCoreBehaviorMode(BehaviorMode.WaitingForTarget);

                } else {

                    EngageOverrideTimer = MyAPIGateway.Session.GameDateTime;
                    ChangeCoreBehaviorMode(BehaviorMode.ApproachTarget);
                    CreateAndMoveToOffset();
                    skipEngageCheck = true;

                }

            }

            //Waiting For Target
            if (_behavior.Mode == BehaviorMode.WaitingForTarget) {

                if (_behavior.AutoPilot.CurrentMode != _behavior.AutoPilot.UserCustomMode) {

                    _behavior.AutoPilot.ActivateAutoPilot(_behavior.RemoteControl.GetPosition(), NewAutoPilotMode.None, CheckEnum.No, CheckEnum.Yes);

                }

                if (_behavior.AutoPilot.Targeting.HasTarget()) {

                    EngageOverrideTimer = MyAPIGateway.Session.GameDateTime;
                    ChangeCoreBehaviorMode(BehaviorMode.ApproachTarget);
                    CreateAndMoveToOffset();
                    skipEngageCheck = true;
                    _behavior.BehaviorTriggerA = true;

                } else if (_behavior.Despawn.NoTargetExpire == true) {

                    _behavior.Despawn.Retreat();

                }

            }

            if (!_behavior.AutoPilot.Targeting.HasTarget() && _behavior.Mode != BehaviorMode.Retreat) {

                ChangeCoreBehaviorMode(BehaviorMode.WaitingForTarget);

            }

            //Approach Target
            if (_behavior.Mode == BehaviorMode.ApproachTarget && !skipEngageCheck) {

                double distance = _behavior.AutoPilot.InGravity() ? this.StrikeBeginPlanetAttackRunDistance : this.StrikeBeginSpaceAttackRunDistance;
                bool engageOverride = false;

                if (EngageOverrideWithDistanceAndTimer) {

                    if (_behavior.AutoPilot.DistanceToCurrentWaypoint < EngageOverrideDistance) {
                    
                        var time = MyAPIGateway.Session.GameDateTime - EngageOverrideTimer;

                        if (time.TotalSeconds > EngageOverrideTimerTrigger) {

                            engageOverride = true;

                        }

                    }
                
                }

                if ((engageOverride || _behavior.AutoPilot.DistanceToCurrentWaypoint <= distance) && _behavior.AutoPilot.Targeting.Target.Distance(_behavior.RemoteControl.GetPosition()) > this.StrikeBreakawayDistance && !_behavior.AutoPilot.IsAvoidingCollision()) {

                    ChangeCoreBehaviorMode(BehaviorMode.EngageTarget);
                    _behavior.AutoPilot.ActivateAutoPilot(_behavior.RemoteControl.GetPosition(), NewAutoPilotMode.RotateToWaypoint | NewAutoPilotMode.ThrustForward | (StrikeEngageUseSafePlanetPathing ? NewAutoPilotMode.PlanetaryPathing : NewAutoPilotMode.None) | NewAutoPilotMode.WaypointFromTarget);
                    skipEngageCheck = true;
                    _behavior.BehaviorTriggerB = true;

                }

                if (skipEngageCheck == false) {

                    var timeSpan = MyAPIGateway.Session.GameDateTime - LastOffsetCalculation;

                    if (timeSpan.TotalSeconds >= StrikeOffsetRecalculationTime) {

                        skipEngageCheck = true;
                        _behavior.AutoPilot.DebugDataA = "Offset Expire, Recalc";
                        CreateAndMoveToOffset();

                    }


                    if (_behavior.AutoPilot.Data.ReverseOffsetDistAltAboveHeight) {

                        if (TargetIsHigh && _behavior.AutoPilot.Targeting.Target.CurrentAltitude() < _behavior.AutoPilot.Data.ReverseOffsetHeight) {

                            TargetIsHigh = false;
                            _behavior.AutoPilot.DebugDataA = "Target is Low";
                            CreateAndMoveToOffset();

                        } else if (!TargetIsHigh && _behavior.AutoPilot.Targeting.Target.CurrentAltitude() > _behavior.AutoPilot.Data.ReverseOffsetHeight) {

                            TargetIsHigh = true;
                            _behavior.AutoPilot.DebugDataA = "Target is High";
                            CreateAndMoveToOffset();

                        }

                    }
                    

                }

            }

            //Engage Target
            if (_behavior.Mode == BehaviorMode.EngageTarget && !skipEngageCheck) {

                BehaviorLogger.Write("Strike: " + StrikeBreakawayDistance.ToString() + " - " + _behavior.AutoPilot.DistanceToInitialWaypoint, BehaviorDebugEnum.General);
                if (_behavior.AutoPilot.DistanceToInitialWaypoint <= StrikeBreakawayDistance || (_behavior.AutoPilot.Data.Unused && _behavior.AutoPilot.Collision.VelocityResult.CollisionImminent())) {

                    EngageOverrideTimer = MyAPIGateway.Session.GameDateTime;
                    ChangeCoreBehaviorMode(BehaviorMode.ApproachTarget);
                    CreateAndMoveToOffset();
                    _behavior.BehaviorTriggerA = true;

                }
            
            }

        }

        public void ChangeCoreBehaviorMode(BehaviorMode newMode) {

            _behavior.ChangeCoreBehaviorMode(newMode);

            if (_defaultCollisionSettings == true) {

                if (_behavior.Mode == BehaviorMode.EngageTarget) {

                    this._behavior.AutoPilot.Data.Unused = UseEngageCollisionEvasion();

                } else {

                    this._behavior.AutoPilot.Data.Unused = true;

                }

            }

        }

        private bool UseEngageCollisionEvasion() {

            return _behavior.AutoPilot.InGravity() ? this.StrikeEngageUseCollisionEvasionPlanet : this.StrikeEngageUseCollisionEvasionSpace;
        
        }


        private void ChangeOffsetAction() {

            return;
            if(_behavior.Mode == BehaviorMode.ApproachTarget)
                _behavior.AutoPilot.ReverseOffsetDirection(70);

        }

        private void CreateAndMoveToOffset() {

            _behavior.AutoPilot.OffsetWaypointGenerator(true);
            LastOffsetCalculation = MyAPIGateway.Session.GameDateTime;
            _behavior.AutoPilot.ActivateAutoPilot(_behavior.RemoteControl.GetPosition(), NewAutoPilotMode.RotateToWaypoint | NewAutoPilotMode.ThrustForward | NewAutoPilotMode.PlanetaryPathing | NewAutoPilotMode.WaypointFromTarget | NewAutoPilotMode.OffsetWaypoint, CheckEnum.Yes, CheckEnum.No);

        }

        public void SetDefaultTags() {

            //Behavior Specific Defaults
            _behavior.AutoPilot.Data = ProfileManager.GetAutopilotProfile("RAI-Generic-Autopilot-Strike");
            _behavior.Despawn.UseNoTargetTimer = true;
            _behavior.AutoPilot.Weapons.UseStaticGuns = true;
            _behavior.AutoPilot.Collision.CollisionTimeTrigger = 5;

        }

        public void InitTags() {

            if (string.IsNullOrWhiteSpace(_behavior.RemoteControl?.CustomData) == false) {

                var descSplit = _behavior.RemoteControl.CustomData.Split('\n');

                foreach (var tag in descSplit) {

                    //StrikeBeginSpaceAttackRunDistance
                    if (tag.Contains("[StrikeBeginSpaceAttackRunDistance:") == true) {

                        TagParse.TagDoubleCheck(tag, ref this.StrikeBeginSpaceAttackRunDistance);

                    }

                    //StrikeBeginPlanetAttackRunDistance
                    if (tag.Contains("[StrikeBeginPlanetAttackRunDistance:") == true) {

                        TagParse.TagDoubleCheck(tag, ref this.StrikeBeginPlanetAttackRunDistance);

                    }

                    //StrikeBreakawayDistance
                    if (tag.Contains("[StrikeBreakawayDistance:") == true) {

                        TagParse.TagDoubleCheck(tag, ref this.StrikeBreakawayDistance);

                    }

                    //StrikeOffsetRecalculationTime
                    if (tag.Contains("[StrikeOffsetRecalculationTime:") == true) {

                        TagParse.TagIntCheck(tag, ref this.StrikeOffsetRecalculationTime);

                    }

                    //StrikeEngageUseSafePlanetPathing
                    if (tag.Contains("[StrikeEngageUseSafePlanetPathing:") == true) {

                        TagParse.TagBoolCheck(tag, ref StrikeEngageUseSafePlanetPathing);

                    }

                }

            }

        }

    }

}
