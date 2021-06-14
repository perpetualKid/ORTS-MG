using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Xna.Framework;

using Orts.Common;
using Orts.Simulation.RollingStocks;

namespace Orts.Simulation.Physics
{
    public partial class Train
    {
        // Default initialisation of starting regions and limits for defining whether complete train is staring or in motion.
        // Typically the train is considereded to be starting if the last car is moving at less then 0.25mps in either direction.
        private const float LastCarZeroSpeedMpS = 0;
        private const float LastCarCompressionMoveSpeedMpS = -0.025f;
        private const float LastCarTensionMoveSpeedMpS = 0.025f;

        private const float AdvancedCouplerDuplicationFactor = 2.0f;


        ///  Sets this train's speed so that momentum is conserved when otherTrain is coupled to it
        internal void SetCoupleSpeed(Train otherTrain, float otherMult)
        {
            float kg1 = 0;
            foreach (TrainCar car in Cars)
                kg1 += car.MassKG;
            float kg2 = 0;
            foreach (TrainCar car in otherTrain.Cars)
                kg2 += car.MassKG;
            SpeedMpS = (kg1 * SpeedMpS + kg2 * otherTrain.SpeedMpS * otherMult) / (kg1 + kg2);
            otherTrain.SpeedMpS = SpeedMpS;
            foreach (TrainCar car1 in Cars)
                //TODO: next code line has been modified to flip trainset physics in order to get viewing direction coincident with loco direction when using rear cab.
                // To achieve the same result with other means, without flipping trainset physics, the line should be changed as follows:
                //                 car1.SpeedMpS = car1.Flipped ? -SpeedMpS : SpeedMpS;
                car1.SpeedMpS = car1.Flipped ^ (car1.IsDriveable && car1.Train.IsActualPlayerTrain && ((MSTSLocomotive)car1).UsingRearCab) ? -SpeedMpS : SpeedMpS;
            foreach (TrainCar car2 in otherTrain.Cars)
                //TODO: next code line has been modified to flip trainset physics in order to get viewing direction coincident with loco direction when using rear cab.
                // To achieve the same result with other means, without flipping trainset physics, the line should be changed as follows:
                //                 car2.SpeedMpS = car2.Flipped ? -SpeedMpS : SpeedMpS;
                car2.SpeedMpS = car2.Flipped ^ (car2.IsDriveable && car2.Train.IsActualPlayerTrain && ((MSTSLocomotive)car2).UsingRearCab) ? -SpeedMpS : SpeedMpS;
        }

        /// setups of the left hand side of the coupler force solving equations
        private void SetupCouplerForceEquations()
        {
            for (int i = 0; i < Cars.Count - 1; i++)
            {
                TrainCar car = Cars[i];
                car.CouplerForceB = 1 / car.MassKG;
                car.CouplerForceA = -car.CouplerForceB;
                car.CouplerForceC = -1 / Cars[i + 1].MassKG;
                car.CouplerForceB -= car.CouplerForceC;
            }
        }

        /// solves coupler force equations
        private void SolveCouplerForceEquations()
        {
            float b = Cars[0].CouplerForceB;
            Cars[0].CouplerForceU = Cars[0].CouplerForceR / b;


            for (int i = 1; i < Cars.Count - 1; i++)
            {
                Cars[i].CouplerForceG = Cars[i - 1].CouplerForceC / b;
                b = Cars[i].CouplerForceB - Cars[i].CouplerForceA * Cars[i].CouplerForceG;
                Cars[i].CouplerForceU = (Cars[i].CouplerForceR - Cars[i].CouplerForceA * Cars[i - 1].CouplerForceU) / b;
            }

            for (int i = Cars.Count - 3; i >= 0; i--)
            {
                Cars[i].CouplerForceU -= Cars[i + 1].CouplerForceG * Cars[i + 1].CouplerForceU;
            }
        }

        /// removes equations if forces don't match faces in contact
        /// returns true if a change is made
        private bool FixCouplerForceEquations()
        {

            // This section zeroes coupler forces if either of the simple or advanced coupler are in Zone 1, ie coupler faces not touching yet.
            // Simple coupler is almost a rigid symetrical couler
            // Advanced coupler can have different zone 1 dimension depending upon coupler type.

            // coupler in tension
            for (int i = 0; i < Cars.Count - 1; i++)
            {
                TrainCar car = Cars[i];
                // if coupler in compression on this car, or coupler is not to be solved, then jump car
                if (car.CouplerSlackM < 0 || car.CouplerForceB >= 1)
                    continue;

                if (car.IsPlayerTrain && !simulator.Settings.SimpleControlPhysics && car.IsAdvancedCoupler) // "Advanced coupler" - operates in three extension zones
                {
                    double MaxZ1TensionM = car.GetMaximumCouplerTensionSlack1M() * AdvancedCouplerDuplicationFactor;
                    // If coupler in Zone 1 tension, ie ( -ve CouplerForceU ) then set coupler forces to zero, as coupler faces not touching yet

                    if (car.CouplerSlackM < MaxZ1TensionM && car.CouplerSlackM >= 0)
                    {
                        car.SetCouplerForce(0);
                        return true;
                    }
                }
                else // "Simple coupler" - only operates on two extension zones, coupler faces not in contact, so set coupler forces to zero
                {
                    float maxs1 = car.GetMaximumSimpleCouplerSlack1M();
                    // In Zone 1 set coupler forces to zero, as coupler faces not touching , or if coupler force is in the opposite direction, ie compressing ( +ve CouplerForceU )
                    if (car.CouplerSlackM < maxs1 || car.CouplerForceU > 0)
                    {
                        car.SetCouplerForce(0);
                        return true;
                    }
                }
            }

            // Coupler in compression
            for (int i = Cars.Count - 1; i >= 0; i--)
            {
                TrainCar car = Cars[i];
                // Coupler in tension on this car or coupler force is "zero" then jump to (process) next car
                if (car.CouplerSlackM > 0 || car.CouplerForceB >= 1)
                    continue;

                if (car.IsPlayerTrain && !simulator.Settings.SimpleControlPhysics && car.IsAdvancedCoupler) // "Advanced coupler" - operates in three extension zones
                {
                    double maxZ1CompressionM = -car.GetMaximumCouplerCompressionSlack1M() * AdvancedCouplerDuplicationFactor;

                    if (car.CouplerSlackM > maxZ1CompressionM && car.CouplerSlackM < 0) // In Zone 1 set coupler forces to zero
                    {
                        car.SetCouplerForce(0);
                        return true;
                    }
                }
                else // "Simple coupler" - only operates on two extension zones, coupler faces not in contact, so set coupler forces to zero
                {

                    float maxs1 = car.GetMaximumSimpleCouplerSlack1M();
                    // In Zone 1 set coupler forces to zero, as coupler faces not touching, or if coupler force is in the opposite direction, ie in tension ( -ve CouplerForceU )
                    if (car.CouplerSlackM > -maxs1 || car.CouplerForceU < 0)
                    {
                        car.SetCouplerForce(0);
                        return true;
                    }
                }
            }
            return false;
        }

        /// removes equations if forces don't match faces in contact
        /// returns true if a change is made
        private bool FixCouplerImpulseForceEquations()
        {
            // This section zeroes impulse coupler forces where there is a force mismatch, ie where coupler is in compression, and a tension force is applied, or vicer versa

            // coupler in tension - CouplerForce (-ve)
            for (int i = Cars.Count - 1; i >= 0; i--)
            {
                TrainCar car = Cars[i];
                // if coupler in compression on this car, or coupler is not to be solved, then jump car
                if (car.CouplerSlackM < 0 || car.CouplerForceB >= 1) // if coupler in compression on this car, or coupler is not to be solved, then jump to next car and skip processing this one
                    continue;
                if (car.IsPlayerTrain && !simulator.Settings.SimpleControlPhysics && car.IsAdvancedCoupler) // "Advanced coupler" - operates in three extension zones
                {
                    float maxZ3TensionM = car.AdvancedCouplerDynamicTensionSlackLimitM;

                    if (car.CouplerSlackM < maxZ3TensionM && car.CouplerSlackM >= 0 || car.CouplerForceU > 0) // If slack is less then coupler maximum extension, then set Impulse forces to zero
                    {
                        car.SetCouplerForce(0);
                        return true;
                    }

                }
                else
                // Simple Coupler
                {
                    // Coupler is in tension according to slack measurement, but a tension force is present
                    if (car.CouplerSlackM < car.CouplerSlack2M || car.CouplerForceU > 0)
                    {
                        car.SetCouplerForce(0);
                        return true;
                    }
                }
            }

            // Coupler in compression - CouplerForce (+ve)
            for (int i = Cars.Count - 1; i >= 0; i--)
            {
                TrainCar car = Cars[i];
                // Coupler in tension on this car or coupler force is "zero" then jump to next car
                if (car.CouplerSlackM > 0 || car.CouplerForceB >= 1)
                    continue;
                if (!simulator.Settings.SimpleControlPhysics && car.IsAdvancedCoupler) // "Advanced coupler" - operates in three extension zones
                {
                    float maxZ3CompressionM = car.AdvancedCouplerDynamicCompressionSlackLimitM;

                    if (car.CouplerSlackM > maxZ3CompressionM && car.CouplerSlackM <= 0 || car.CouplerForceU < 0) // If slack is less then coupler maximum extension, then set Impulse forces to zero
                    {
                        car.SetCouplerForce(0);
                        return true;
                    }
                }
                else // Simple coupler
                {
                    if (car.CouplerSlackM > -car.CouplerSlack2M || car.CouplerForceU < 0)
                    {
                        car.SetCouplerForce(0);
                        return true;
                    }
                }
            }
            return false;
        }

        /// computes and applies coupler impulse forces which force speeds to match when no relative movement is possible
        private void AddCouplerImpulseForces(double elapsedTime)
        {
            _ = elapsedTime;
            if (Cars.Count < 2)
                return;
            SetupCouplerForceEquations();

            for (int i = 0; i < Cars.Count - 1; i++)
            {
                TrainCar car = Cars[i];

                if (car.IsPlayerTrain && !simulator.Settings.SimpleControlPhysics && car.IsAdvancedCoupler) // "Advanced coupler"
                {
                    float maxTensionCouplerLimitM = car.AdvancedCouplerDynamicTensionSlackLimitM;
                    float maxCompressionCouplerLimitM = car.AdvancedCouplerDynamicCompressionSlackLimitM;

                    if (maxCompressionCouplerLimitM < car.CouplerSlackM && car.CouplerSlackM < maxTensionCouplerLimitM)
                    {
                        car.CouplerForceB = 1;
                        car.CouplerForceA = car.CouplerForceC = car.CouplerForceR = 0;
                    }
                    else
                        car.CouplerForceR = Cars[i + 1].SpeedMpS - car.SpeedMpS;
                }

                else // Simple coupler - set impulse force to zero if coupler slack has not exceeded zone 2 limit
                {
                    float max = car.CouplerSlack2M;
                    if (-max < car.CouplerSlackM && car.CouplerSlackM < max)
                    {
                        car.CouplerForceB = 1;
                        car.CouplerForceA = car.CouplerForceC = car.CouplerForceR = 0;
                    }
                    else
                        car.CouplerForceR = Cars[i + 1].SpeedMpS - car.SpeedMpS;
                }
            }

            do
                SolveCouplerForceEquations();
            while (FixCouplerImpulseForceEquations());

            maximumCouplerForceN = 0;
            for (int i = 0; i < Cars.Count - 1; i++)
            {
                TrainCar car = Cars[i];

                // save impulse coupler force as it will be overwritten by "static" coupler force
                car.ImpulseCouplerForceUN = car.CouplerForceU;

                // This section seems to be required to get car moving
                if (car.IsPlayerTrain && !simulator.Settings.SimpleControlPhysics && car.IsAdvancedCoupler) // "Advanced coupler"
                {
                    Cars[i].SpeedMpS += Cars[i].CouplerForceU / Cars[i].MassKG;
                    Cars[i + 1].SpeedMpS -= Cars[i].CouplerForceU / Cars[i + 1].MassKG;

                    // This ensures that the last car speed never goes negative - as this might cause a sudden jerk in the train when viewed.
                    if (i == Cars.Count - 2)
                    {
                        if (FirstCar.SpeedMpS > 0 && Cars[i + 1].SpeedMpS < 0)
                        {
                            Cars[i + 1].SpeedMpS = 0;
                            //Trace.TraceInformation("Last Car Zero Speed Set - CarID {0} - -ve set +ve", car.CarID);
                        }
                        else if (FirstCar.SpeedMpS < 0 && Cars[i + 1].SpeedMpS > 0)
                        {
                            Cars[i + 1].SpeedMpS = 0;
                            //Trace.TraceInformation("Last Car Zero Speed Set - CarID {0} - +ve set -ve", car.CarID);
                        }
                    }
                }
                else // Simple Coupler
                {
                    Cars[i].SpeedMpS += Cars[i].CouplerForceU / Cars[i].MassKG;
                    Cars[i + 1].SpeedMpS -= Cars[i].CouplerForceU / Cars[i + 1].MassKG;
                }
            }
        }

        /// <summary>
        /// computes coupler acceleration balancing forces for Coupler
        /// The couplers are calculated using the formulas 9.7 to 9.9 (pg 243), described in the Handbook of Railway Vehicle Dynamics by Simon Iwnicki
        ///  In the book there is one equation per car and in OR there is one equation per coupler. To get the OR equations, first solve the 
        ///  equations in the book for acceleration. Then equate the acceleration equation for each pair of adjacent cars. Arrange the fwc 
        ///  terms on the left hand side and all other terms on the right side. Now if the fwc values are treated as unknowns, there is a 
        ///  tridiagonal system of linear equations which can be solved to find the coupler forces needed to make the accelerations match.
        ///  
        ///  Each fwc value corresponds to one of the CouplerForceU values.The CouplerForceA, CouplerForceB and CouplerForceC values are 
        ///  the CouplerForceU coefficients for the previuous coupler, the current coupler and the next coupler.The CouplerForceR values are 
        ///  the sum of the right hand side terms. The notation and the code in SolveCouplerForceEquations() that solves for the CouplerForceU 
        ///  values is from "Numerical Recipes in C".
        ///  
        /// Or has two coupler models - Simple and Advanced
        /// Simple - has two extension zones - #1 where the coupler faces have not come into contact, and hence CouplerForceU is zero, #2 where coupler 
        /// forces are taking the full weight of the following car. The breaking capacity of the coupler could be considered zone 3
        /// 
        /// Advanced - has three extension zones, and the breaking zone - #1 where the coupler faces have not come into contact, and hence 
        /// CouplerForceU is zero, #2 where the spring is taking the load, and car is able to oscilate in the train as it moves backwards and 
        /// forwards due to the action of the spring, #3 - where the coupler is fully extended against the friction brake, and the full force of the 
        /// following wagons will be applied to the coupler.
        /// 
        /// Coupler Force (CouplerForceU) : Fwd = -ve, Rev = +ve,  Total Force (TotalForceN): Fwd = -ve, Rev = +ve
        /// 
        /// <\summary>
        private void ComputeCouplerForces(double elapsedTime)
        {

            // TODO: this loop could be extracted and become a separate method, that could be called also by TTTrain.physicsPreUpdate
            for (int i = 0; i < Cars.Count; i++)
            {
                // If car is moving then the raw total force on each car is adjusted according to changing forces.
                if (Cars[i].SpeedMpS > 0)
                    Cars[i].TotalForceN -= (Cars[i].FrictionForceN + Cars[i].BrakeForceN + Cars[i].CurveForceN + Cars[i].WindForceN + Cars[i].TunnelForceN);
                else if (Cars[i].SpeedMpS < 0)
                    Cars[i].TotalForceN += Cars[i].FrictionForceN + Cars[i].BrakeForceN + Cars[i].CurveForceN + Cars[i].WindForceN + Cars[i].TunnelForceN;
            }

            if (Cars.Count < 2)
                return;

            SetupCouplerForceEquations(); // Based upon the car Mass, set up LH side forces (ABC) parameters

            // Calculate RH side coupler force
            // Whilever coupler faces not in contact, then "zero coupler force" by setting A = C = R = 0
            // otherwise R is calculated based on difference in acceleration between cars, or stiffness and damping value

            for (int i = 0; i < Cars.Count - 1; i++)
            {
                TrainCar car = Cars[i];
                if (car.IsPlayerTrain && !simulator.Settings.SimpleControlPhysics && car.IsAdvancedCoupler) // "Advanced coupler" - operates in three extension zones
                {

                    //Force on coupler is set so that no force is applied until coupler faces come into contact with each other
                    float maxZ1TensionM = car.GetMaximumCouplerTensionSlack1M();
                    float maxZ1CompressionM = -car.GetMaximumCouplerCompressionSlack1M();

                    double IndividualCouplerSlackM = car.CouplerSlackM / AdvancedCouplerDuplicationFactor;

                    if (IndividualCouplerSlackM >= maxZ1CompressionM && IndividualCouplerSlackM <= maxZ1TensionM) // Zone 1 coupler faces not in contact - no force generated
                    {
                        car.CouplerForceB = 1;
                        car.CouplerForceA = car.CouplerForceC = car.CouplerForceR = 0;
                    }
                    else
                    {
                        car.CouplerForceR = Cars[i + 1].TotalForceN / Cars[i + 1].MassKG - car.TotalForceN / car.MassKG;
                    }

                }
                else // "Simple coupler" - only operates on two extension zones, coupler faces not in contact, so set coupler forces to zero
                {
                    float max = car.GetMaximumSimpleCouplerSlack1M();
                    if (-max < car.CouplerSlackM && car.CouplerSlackM < max)
                    {
                        car.CouplerForceB = 1;
                        car.CouplerForceA = car.CouplerForceC = car.CouplerForceR = 0;
                    }
                    else
                        car.CouplerForceR = Cars[i + 1].TotalForceN / Cars[i + 1].MassKG - car.TotalForceN / car.MassKG;
                }
            }

            // Solve coupler forces to find CouplerForceU
            do
                SolveCouplerForceEquations();
            while (FixCouplerForceEquations());

            for (int i = 0; i < Cars.Count - 1; i++)
            {
                // Calculate total forces on cars
                TrainCar car = Cars[i];

                // Check to make sure that last car does not have any coulpler force on its coupler (no cars connected). When cars reversed, there is sometimes a "residual" coupler force.
                if (i == Cars.Count - 1 && Cars[i + 1].CouplerForceU != 0)
                {
                    Cars[i].CouplerForceU = 0;
                }

                car.CouplerForceUSmoothed.Update(elapsedTime, car.CouplerForceU);
                car.SmoothedCouplerForceUN = (float)car.CouplerForceUSmoothed.SmoothedValue;

                // Total force acting on each car is adjusted depending upon the calculated coupler forces
                car.TotalForceN += car.CouplerForceU;
                Cars[i + 1].TotalForceN -= car.CouplerForceU;

                // Find max coupler force on the car - currently doesn't appear to be used anywhere
                if (maximumCouplerForceN < Math.Abs(car.CouplerForceU))
                    maximumCouplerForceN = Math.Abs(car.CouplerForceU);

                // Update coupler slack which acts as the  upper limit in slack calculations
                // For the advanced coupler the slack limit is "dynamic", and depends upon the force applied to the coupler, and hence how far it will extend. 
                // This gives the effect that coupler extension will decrease down the train as the coupler force decreases. CouplerForce has a small smoothing 
                // effect to redcuce jerk, especially when starting.

                // As a coupler is defined in terms of force for one car only, then force/slack calculations need to be done with half the slack (IndividualCouplerSlackM) for calculation puposes.
                // The calculated slack will then be doubled to compensate.

                // The location of each car in the train is referenced from the last car in the train. Hence when starting a jerking motion can be present if the last car is still stationary
                // and the coupler slack increases and decreases along the train. This section of the code attempts to reduce this jerking motion by holding the coupler extension (slack) distance
                // to a "fixed" value until the last car has commenced moving. This is consistent with real life as the coupler would be extended as each car starts moving. 
                // A damping factor is also used to reduce any large variations during train start. CouplerForce is also smoothed slightly to also reduce any jerkiness

                if (car.IsPlayerTrain && !simulator.Settings.SimpleControlPhysics && car.IsAdvancedCoupler) // "Advanced coupler" - operates in three extension zones
                {

                    // Note different slack lengths can be used depending upon whether the coupler is in tension or compression
                    // Rigid couplers use a fixed limit, and there is no variation.
                    float maxZ1TensionM = car.GetMaximumCouplerTensionSlack1M();
                    float maxZ2TensionM = car.GetMaximumCouplerTensionSlack2M();
                    float maxZ3TensionM = car.GetMaximumCouplerTensionSlack3M();
                    float maxZ1CompressionM = -car.GetMaximumCouplerCompressionSlack1M();
                    float maxZ2CompressionM = -car.GetMaximumCouplerCompressionSlack2M();
                    float maxZ3CompressionM = -car.GetMaximumCouplerCompressionSlack3M();

                    float couplerChangeDampingFactor;

                    // The magnitude of the change factor is varied depending upon whether the train is completely in motion, or is just starting.
                    if (LastCar.SpeedMpS != FirstCar.SpeedMpS && LastCar.SpeedMpS > LastCarCompressionMoveSpeedMpS && LastCar.SpeedMpS < LastCarTensionMoveSpeedMpS)
                    {
                        couplerChangeDampingFactor = 0.99f;
                    }
                    else if (LastCar.SpeedMpS == FirstCar.SpeedMpS)
                    {
                        couplerChangeDampingFactor = 0.98f;
                    }
                    else
                    {
                        couplerChangeDampingFactor = 0.98f;
                    }

                    // Default initialisation of limits
                    car.AdvancedCouplerDynamicTensionSlackLimitM = maxZ3TensionM * AdvancedCouplerDuplicationFactor;
                    car.AdvancedCouplerDynamicCompressionSlackLimitM = maxZ3CompressionM * AdvancedCouplerDuplicationFactor;
                    bool rigidCoupler = car.GetCouplerRigidIndication();

                    // For calculation purposes use only have the individual coupler distance between each car for calculations.
                    double individualCouplerSlackM = car.CouplerSlackM / AdvancedCouplerDuplicationFactor;

                    if (car.SmoothedCouplerForceUN < 0) // Tension
                    {
                        if (!rigidCoupler)
                        {

                            if (individualCouplerSlackM < 0 && FirstCar.SpeedMpS > 0 && LastCar.SpeedMpS >= LastCarZeroSpeedMpS && LastCar.SpeedMpS < LastCarTensionMoveSpeedMpS && LeadLocomotive.Direction == MidpointDirection.Forward)
                            {
                                // Whilst train is starting in forward direction, don't allow negative coupler slack.

                                float diff = car.PreviousCouplerSlackM * (1.0f - couplerChangeDampingFactor);
                                if (car.PreviousCouplerSlackM - diff > individualCouplerSlackM)
                                {
                                    car.CouplerSlackM = car.PreviousCouplerSlackM - diff;
                                    car.AdvancedCouplerDynamicTensionSlackLimitM = car.CouplerSlackM;
                                }

                                //Trace.TraceInformation("Tension Slack -ve : CarID {0} Force {1} Slack {2} PrevSlack {3} TempDiff {4}", car.CarID, car. , car.CouplerSlackM, car.PreviousCouplerSlackM, TempDiff);
                            }
                            else if (FirstCar.SpeedMpS < 0 && LastCar.SpeedMpS > LastCarCompressionMoveSpeedMpS && LastCar.SpeedMpS <= LastCarZeroSpeedMpS && LeadLocomotive.Direction == MidpointDirection.Reverse)
                            {
                                if (individualCouplerSlackM > 0)
                                {
                                    // Train is starting to move reverse, don't allow positive coupler slack - should either be negative or zero
                                    car.CouplerSlackM = car.PreviousCouplerSlackM;
                                    car.CouplerSlackM = MathHelper.Clamp(car.CouplerSlackM, car.CouplerSlackM, 0);
                                    car.AdvancedCouplerDynamicCompressionSlackLimitM = car.CouplerSlackM;
                                }
                                else if (individualCouplerSlackM < maxZ1CompressionM)
                                {
                                    car.CouplerSlackM = maxZ1CompressionM * AdvancedCouplerDuplicationFactor;
                                    car.AdvancedCouplerDynamicCompressionSlackLimitM = car.CouplerSlackM;
                                }
                            }
                            else if (individualCouplerSlackM > maxZ1TensionM && individualCouplerSlackM <= maxZ3TensionM)
                            {
                                // A linear curve is assumed for coupler stiffness - this curve is then used to calculate the amount of slack that the coupler should have. 
                                //These values are set to "lock" the coupler at this maximum slack length

                                if (Math.Abs(car.SmoothedCouplerForceUN) < car.GetCouplerTensionStiffness1N())
                                {
                                    // Calculate coupler slack based upon force on coupler
                                    float slackDiff = maxZ2TensionM - maxZ1TensionM;
                                    float gradStiffness = car.GetCouplerTensionStiffness1N() / (slackDiff); // Calculate gradient of line
                                    float computedZone2SlackM = (Math.Abs(car.SmoothedCouplerForceUN) / gradStiffness) + maxZ1TensionM; // Current slack distance in this zone of coupler

                                    if (LastCar.SpeedMpS >= LastCarZeroSpeedMpS && LastCar.SpeedMpS < LastCarTensionMoveSpeedMpS)
                                    {
                                        // Whilst train is starting
                                        if (computedZone2SlackM < (Math.Abs(car.PreviousCouplerSlackM) / AdvancedCouplerDuplicationFactor))
                                        {
                                            // Train is starting, don't allow coupler slack to decrease untill complete train is moving
                                            car.CouplerSlackM = car.PreviousCouplerSlackM;
                                            car.AdvancedCouplerDynamicTensionSlackLimitM = car.CouplerSlackM;
                                        }
                                        else if (computedZone2SlackM > Math.Abs(car.PreviousCouplerSlackM) / AdvancedCouplerDuplicationFactor)
                                        {
                                            // Allow coupler slack to slowly increase
                                            // Increase slack value
                                            car.CouplerSlackM = car.PreviousCouplerSlackM * (1.0f / couplerChangeDampingFactor);
                                            car.CouplerSlackM = MathHelper.Clamp(car.CouplerSlackM, 0, maxZ3TensionM * AdvancedCouplerDuplicationFactor);
                                            car.AdvancedCouplerDynamicTensionSlackLimitM = car.CouplerSlackM;
                                        }
                                    }
                                    //   else if (ComputedZone2SlackM < IndividualCouplerSlackM)
                                    else if (computedZone2SlackM < (Math.Abs(car.PreviousCouplerSlackM) / AdvancedCouplerDuplicationFactor))
                                    {
                                        // Once train is moving then allow gradual reduction in coupler slack
                                        //    car.CouplerSlackM = ComputedZone2SlackM * AdvancedCouplerDuplicationFactor * CouplerChangeDampingFactor;
                                        car.CouplerSlackM = car.PreviousCouplerSlackM * couplerChangeDampingFactor;
                                        car.AdvancedCouplerDynamicTensionSlackLimitM = car.CouplerSlackM;
                                    }
                                    else
                                    //       else if (ComputedZone2SlackM > IndividualCouplerSlackM)
                                    {
                                        // If train moving then allow coupler slack to increase depending upon the caclulated slack
                                        //    car.CouplerSlackM = ComputedZone2SlackM * AdvancedCouplerDuplicationFactor * (1.0f / CouplerChangeDampingFactor);
                                        car.CouplerSlackM = computedZone2SlackM * AdvancedCouplerDuplicationFactor * couplerChangeDampingFactor;
                                        car.CouplerSlackM = MathHelper.Clamp(car.CouplerSlackM, 0, maxZ3TensionM * AdvancedCouplerDuplicationFactor);
                                        car.AdvancedCouplerDynamicTensionSlackLimitM = car.CouplerSlackM;
                                    }

                                    //   Trace.TraceInformation("Zone 2 Tension - ID {0} Diff {1} Stiff {2} SmoothForce {3} CouplerForceN {4} Slack {5} ComputedSlack {6} MaxZ2 {7} IndSlack {8} LastSpeed {9} FinalDiff {10} ChangeFactror {11}", car.CarID, SlackDiff, GradStiffness, car.SmoothedCouplerForceUN, car.CouplerForceU, car.CouplerSlackM, ComputedZone2SlackM, MaxZ2TensionM, IndividualCouplerSlackM, LastCar.SpeedMpS, (car.CouplerSlackM - (IndividualCouplerSlackM * AdvancedCouplerDuplicationFactor)), CouplerChangeDampingFactor);
                                }
                                else
                                {
                                    // A linear curve is assumed for coupler stiffness - this curve is then used to calculate the amount of slack that the coupler should have. 
                                    //These values are set to "lock" the coupler at this maximum slack length
                                    float slackDiff = maxZ3TensionM - maxZ2TensionM;
                                    float gradStiffness = (car.GetCouplerTensionStiffness2N() - car.GetCouplerTensionStiffness1N()) / (slackDiff);
                                    float computedZone3SlackM = (Math.Abs(car.SmoothedCouplerForceUN) / gradStiffness) + maxZ2TensionM;

                                    if (LastCar.SpeedMpS >= LastCarZeroSpeedMpS && LastCar.SpeedMpS < LastCarTensionMoveSpeedMpS)
                                    {
                                        // Train is starting, don't allow coupler slack to decrease until complete train is moving
                                        if (computedZone3SlackM < (Math.Abs(car.PreviousCouplerSlackM) / AdvancedCouplerDuplicationFactor))
                                        {
                                            car.CouplerSlackM = car.PreviousCouplerSlackM;
                                            car.AdvancedCouplerDynamicTensionSlackLimitM = car.CouplerSlackM;
                                        }
                                        else if (computedZone3SlackM > Math.Abs(car.PreviousCouplerSlackM) / AdvancedCouplerDuplicationFactor)
                                        {
                                            // Allow coupler slack to slowly increase

                                            // Increase slack value
                                            car.CouplerSlackM = car.PreviousCouplerSlackM * (1.0f / couplerChangeDampingFactor);
                                            car.CouplerSlackM = MathHelper.Clamp(car.CouplerSlackM, 0, maxZ3TensionM * AdvancedCouplerDuplicationFactor);
                                            car.AdvancedCouplerDynamicTensionSlackLimitM = car.CouplerSlackM;
                                        }
                                    }
                                    // else if (ComputedZone3SlackM < IndividualCouplerSlackM)
                                    else if (computedZone3SlackM < (Math.Abs(car.PreviousCouplerSlackM) / AdvancedCouplerDuplicationFactor))
                                    {
                                        // Decrease coupler slack - moving
                                        // car.CouplerSlackM = ComputedZone3SlackM * AdvancedCouplerDuplicationFactor * CouplerChangeDampingFactor;
                                        car.CouplerSlackM = car.PreviousCouplerSlackM * couplerChangeDampingFactor;
                                        car.AdvancedCouplerDynamicTensionSlackLimitM = car.CouplerSlackM;
                                    }
                                    else
                                    //     else if (ComputedZone3SlackM > IndividualCouplerSlackM)
                                    {
                                        // Allow coupler slack to be increased - moving
                                        car.CouplerSlackM = computedZone3SlackM * AdvancedCouplerDuplicationFactor * couplerChangeDampingFactor;
                                        //    car.CouplerSlackM = ComputedZone3SlackM * AdvancedCouplerDuplicationFactor * (1.0f / CouplerChangeDampingFactor);
                                        car.CouplerSlackM = MathHelper.Clamp(car.CouplerSlackM, 0, maxZ3TensionM * AdvancedCouplerDuplicationFactor);
                                        car.AdvancedCouplerDynamicTensionSlackLimitM = car.CouplerSlackM;
                                    }

                                    //   Trace.TraceInformation("Zone 3 Tension - ID {0} Diff {1} Stiff {2} SmoothForce {3} CouplerForceN {4} Slack {5} ComputedSlack {6} MaxZ3 {7} IndSlack {8} LastSpeed {9} FinalDiff {10} ChangeFactror {11}", car.CarID, SlackDiff, GradStiffness, car.SmoothedCouplerForceUN, car.CouplerForceU, car.CouplerSlackM, ComputedZone3SlackM, MaxZ3TensionM, IndividualCouplerSlackM, LastCar.SpeedMpS, (car.CouplerSlackM - (IndividualCouplerSlackM * AdvancedCouplerDuplicationFactor)), CouplerChangeDampingFactor);
                                }
                            }
                            else if (individualCouplerSlackM > maxZ3TensionM)  // Make sure that a new computed slack value does not take slack into the next zone.
                            {
                                // If computed slack is higher then Zone 3 limit, then set to max Z3. 
                                if (LastCar.SpeedMpS >= LastCarZeroSpeedMpS && LastCar.SpeedMpS < LastCarTensionMoveSpeedMpS)
                                {
                                    car.CouplerSlackM = maxZ3TensionM * AdvancedCouplerDuplicationFactor;
                                    car.AdvancedCouplerDynamicTensionSlackLimitM = car.CouplerSlackM;
                                }
                                else
                                {
                                    // A linear curve is assumed for coupler stiffness - this curve is then used to calculate the amount of slack that the coupler should have. 
                                    //These values are set to "lock" the coupler at this maximum slack length
                                    float slackDiff = maxZ3TensionM - maxZ2TensionM;
                                    float gradStiffness = (car.GetCouplerTensionStiffness2N() - car.GetCouplerTensionStiffness1N()) / (slackDiff);
                                    float computedZone4SlackM = (Math.Abs(car.SmoothedCouplerForceUN) / gradStiffness) + maxZ2TensionM;

                                    if (computedZone4SlackM < maxZ3TensionM && computedZone4SlackM > car.PreviousCouplerSlackM / AdvancedCouplerDuplicationFactor)
                                    {
                                        // Increase coupler slack
                                        car.CouplerSlackM = computedZone4SlackM * AdvancedCouplerDuplicationFactor * (1.0f / couplerChangeDampingFactor);
                                        car.CouplerSlackM = MathHelper.Clamp(car.CouplerSlackM, 0, maxZ3TensionM * AdvancedCouplerDuplicationFactor);
                                        car.AdvancedCouplerDynamicTensionSlackLimitM = car.CouplerSlackM;
                                    }
                                    else if (computedZone4SlackM > maxZ3TensionM)
                                    {
                                        car.CouplerSlackM = maxZ3TensionM * AdvancedCouplerDuplicationFactor;
                                        car.AdvancedCouplerDynamicTensionSlackLimitM = car.CouplerSlackM;
                                    }
                                    else if (computedZone4SlackM < maxZ3TensionM && computedZone4SlackM < car.PreviousCouplerSlackM / AdvancedCouplerDuplicationFactor)
                                    {
                                        // Decrease coupler slack
                                        car.CouplerSlackM = car.PreviousCouplerSlackM * couplerChangeDampingFactor;
                                        car.AdvancedCouplerDynamicTensionSlackLimitM = car.CouplerSlackM;
                                    }
                                }
                            }
                            //Trace.TraceInformation("Zone Tension - ID {0} SmoothForce {1} CouplerForceN {2} Slack {3} Speed {4} Loop {5}", car.CarID, car.SmoothedCouplerForceUN, car.CouplerForceU, car.CouplerSlackM, car.SpeedMpS, Loop);
                        }
                    }
                    else if (car.SmoothedCouplerForceUN == 0) // In this instance the coupler slack must be greater then the Z1 limit, as no coupler force is generated, and train will not move.
                    {
                        if (car.SpeedMpS == 0)
                        {
                            // In this instance the coupler slack must be greater then the Z1 limit, otherwise no coupler force is generated, and train will not move.
                            car.AdvancedCouplerDynamicTensionSlackLimitM = maxZ1TensionM * AdvancedCouplerDuplicationFactor * 1.05f;
                            car.AdvancedCouplerDynamicCompressionSlackLimitM = maxZ1CompressionM * AdvancedCouplerDuplicationFactor * 1.05f;

                            if (car.CouplerSlackM < 0 && LastCar.SpeedMpS >= 0 && LastCar.SpeedMpS < LastCarTensionMoveSpeedMpS && FirstCar.SpeedMpS > 0)
                            {
                                // When starting in forward we don't want to allow slack to go negative
                                car.CouplerSlackM = car.PreviousCouplerSlackM;
                                // Make sure that coupler slack never goes negative when train starting and moving forward and starting
                                car.CouplerSlackM = MathHelper.Clamp(car.CouplerSlackM, 0, car.CouplerSlackM);
                            }
                            else if (car.CouplerSlackM > 0 && LastCar.SpeedMpS <= 0 && LastCar.SpeedMpS > LastCarCompressionMoveSpeedMpS && FirstCar.SpeedMpS < 0)
                            {
                                // When starting in reverse we don't want to allow slack to go positive
                                car.CouplerSlackM = car.PreviousCouplerSlackM;
                                // Make sure that coupler slack never goes positive when train starting and moving reverse and starting
                                car.CouplerSlackM = MathHelper.Clamp(car.CouplerSlackM, car.CouplerSlackM, 0);
                            }
                        }
                        //if (car.SpeedMpS != 0)
                        //  Trace.TraceInformation("Advanced - Zero coupler force - CarID {0} Slack {1} Loop {2} Speed {3} Previous {4}", car.CarID, car.CouplerSlackM, Loop, car.SpeedMpS, car.PreviousCouplerSlackM);
                    }
                    else   // Compression
                    {
                        if (!rigidCoupler)
                        {
                            if (individualCouplerSlackM > 0 && FirstCar.SpeedMpS < 0 && LastCar.SpeedMpS <= LastCarZeroSpeedMpS && LastCar.SpeedMpS > LastCarCompressionMoveSpeedMpS && LeadLocomotive.Direction == MidpointDirection.Reverse)
                            {
                                // Train is moving in reverse, don't allow positive coupler slack.
                                float diff = Math.Abs(car.PreviousCouplerSlackM) * (1.0f - couplerChangeDampingFactor);
                                if (Math.Abs(car.PreviousCouplerSlackM) - diff > Math.Abs(individualCouplerSlackM))
                                {
                                    car.CouplerSlackM = -1.0f * Math.Abs(car.PreviousCouplerSlackM) - diff;
                                    car.AdvancedCouplerDynamicCompressionSlackLimitM = car.CouplerSlackM;
                                }
                            }
                            else if (FirstCar.SpeedMpS > 0 && LastCar.SpeedMpS >= LastCarZeroSpeedMpS && LastCar.SpeedMpS < LastCarTensionMoveSpeedMpS && LeadLocomotive.Direction == MidpointDirection.Forward)
                            {
                                if (individualCouplerSlackM < 0)
                                {
                                    // Train is starting to move forward, don't allow negative coupler slack - should either be positive or zero
                                    car.CouplerSlackM = car.PreviousCouplerSlackM;
                                    car.CouplerSlackM = MathHelper.Clamp(car.CouplerSlackM, 0, car.CouplerSlackM);
                                    car.AdvancedCouplerDynamicTensionSlackLimitM = car.CouplerSlackM;
                                }
                                else if (individualCouplerSlackM > maxZ1TensionM)
                                {
                                    car.CouplerSlackM = maxZ1TensionM * AdvancedCouplerDuplicationFactor;
                                    car.AdvancedCouplerDynamicTensionSlackLimitM = car.CouplerSlackM;
                                }
                            }
                            else if (maxZ3CompressionM < individualCouplerSlackM && individualCouplerSlackM <= maxZ1CompressionM)
                            {

                                // A linear curve is assumed for coupler stiffness - this curve is then used to calculate the amount of slack that the coupler should have. 
                                //These values are set to "lock" the coupler at this maximum slack length

                                if (Math.Abs(car.SmoothedCouplerForceUN) < car.GetCouplerCompressionStiffness1N())
                                {
                                    float slackDiff = Math.Abs(maxZ2CompressionM - maxZ1CompressionM);
                                    float gradStiffness = car.GetCouplerCompressionStiffness1N() / (slackDiff); // Calculate gradient of line
                                    float computedZone2SlackM = (Math.Abs(car.SmoothedCouplerForceUN) / gradStiffness) + Math.Abs(maxZ1CompressionM); // Current slack distance in this zone of coupler

                                    if (LastCar.SpeedMpS > LastCarCompressionMoveSpeedMpS && LastCar.SpeedMpS <= LastCarZeroSpeedMpS)
                                    {

                                        if (computedZone2SlackM < (Math.Abs(car.PreviousCouplerSlackM) / AdvancedCouplerDuplicationFactor))
                                        {
                                            // Train is starting, don't allow coupler slack to decrease until complete train is moving
                                            car.CouplerSlackM = car.PreviousCouplerSlackM;
                                            car.AdvancedCouplerDynamicCompressionSlackLimitM = car.CouplerSlackM;
                                        }
                                        else if (computedZone2SlackM > Math.Abs(car.PreviousCouplerSlackM) / AdvancedCouplerDuplicationFactor)
                                        {
                                            // Allow coupler slack to slowly increase

                                            // Increase coupler slack slowly
                                            car.CouplerSlackM = car.PreviousCouplerSlackM * (1.0f / couplerChangeDampingFactor);
                                            car.CouplerSlackM = MathHelper.Clamp(car.CouplerSlackM, maxZ3CompressionM * AdvancedCouplerDuplicationFactor, 0);
                                            car.AdvancedCouplerDynamicCompressionSlackLimitM = car.CouplerSlackM;
                                        }
                                    }
                                    else if (computedZone2SlackM < (Math.Abs(car.PreviousCouplerSlackM) / AdvancedCouplerDuplicationFactor))
                                    {
                                        // Once train is moving then allow gradual reduction in coupler slack
                                        car.CouplerSlackM = car.PreviousCouplerSlackM * couplerChangeDampingFactor;
                                        car.AdvancedCouplerDynamicCompressionSlackLimitM = car.CouplerSlackM;
                                    }
                                    else
                                    //                              else if (ComputedZone2SlackM > Math.Abs(IndividualCouplerSlackM))
                                    {
                                        // If train moving then allow coupler slack to increase slowly depending upon the caclulated slack
                                        car.CouplerSlackM = -1.0f * computedZone2SlackM * AdvancedCouplerDuplicationFactor * couplerChangeDampingFactor;
                                        car.CouplerSlackM = MathHelper.Clamp(car.CouplerSlackM, maxZ3CompressionM * AdvancedCouplerDuplicationFactor, 0);
                                        car.AdvancedCouplerDynamicCompressionSlackLimitM = car.CouplerSlackM;
                                    }

                                    //   Trace.TraceInformation("Zone 2 Compression - ID {0} Diff {1} Stiff {2} SmoothForce {3} CouplerForceN {4} Slack {5} ComputedSlack {6} MaxZ2 {7} IndSlack {8} LastSpeed {9} FinalDiff {10} ChangeFactror {11}", car.CarID, SlackDiff, GradStiffness, car.SmoothedCouplerForceUN, car.CouplerForceU, car.CouplerSlackM, ComputedZone2SlackM, MaxZ2TensionM, IndividualCouplerSlackM, LastCar.SpeedMpS, (car.CouplerSlackM - (IndividualCouplerSlackM * AdvancedCouplerDuplicationFactor)), CouplerChangeDampingFactor);
                                }
                                else
                                {
                                    // A linear curve is assumed for coupler stiffness - this curve is then used to calculate the amount of slack that the coupler should have. 
                                    //These values are set to "lock" the coupler at this maximum slack length
                                    float slackDiff = Math.Abs(maxZ3CompressionM - maxZ2CompressionM);
                                    float gradStiffness = (car.GetCouplerCompressionStiffness2N() - car.GetCouplerCompressionStiffness1N()) / (slackDiff);
                                    float computedZone3SlackM = (Math.Abs(car.SmoothedCouplerForceUN) / gradStiffness) + Math.Abs(maxZ2CompressionM);

                                    if (LastCar.SpeedMpS > LastCarCompressionMoveSpeedMpS && LastCar.SpeedMpS <= LastCarZeroSpeedMpS)
                                    {
                                        // Train is starting, don't allow coupler slack to decrease until complete train is moving
                                        if (computedZone3SlackM < (Math.Abs(car.PreviousCouplerSlackM) / AdvancedCouplerDuplicationFactor))
                                        {
                                            car.CouplerSlackM = car.PreviousCouplerSlackM;
                                            car.AdvancedCouplerDynamicCompressionSlackLimitM = car.CouplerSlackM;
                                        }
                                        else if (computedZone3SlackM > Math.Abs(car.PreviousCouplerSlackM) / AdvancedCouplerDuplicationFactor)
                                        {
                                            // Increase slack
                                            car.CouplerSlackM = car.PreviousCouplerSlackM * (1.0f / couplerChangeDampingFactor);
                                            car.CouplerSlackM = MathHelper.Clamp(car.CouplerSlackM, maxZ3CompressionM * AdvancedCouplerDuplicationFactor, 0);
                                            car.AdvancedCouplerDynamicCompressionSlackLimitM = car.CouplerSlackM;
                                        }
                                        //   Trace.TraceInformation("Zone 3 Compression - ID {0} Diff {1} Stiff {2} SmoothForce {3} CouplerForceN {4} Slack {5} ComputedSlack {6} MaxZ2 {7} IndSlack {8} LastSpeed {9} FinalDiff {10} ChangeFactror {11}", car.CarID, SlackDiff, GradStiffness, car.SmoothedCouplerForceUN, car.CouplerForceU, car.CouplerSlackM, ComputedZone3SlackM, MaxZ2TensionM, IndividualCouplerSlackM, LastCar.SpeedMpS, (car.CouplerSlackM - (IndividualCouplerSlackM * AdvancedCouplerDuplicationFactor)), CouplerChangeDampingFactor);
                                    }
                                    else if (computedZone3SlackM < (Math.Abs(car.PreviousCouplerSlackM) / AdvancedCouplerDuplicationFactor))
                                    {
                                        // Train moving - Decrease slack if Computed Slack is less then the previous slack value
                                        car.CouplerSlackM = car.PreviousCouplerSlackM * couplerChangeDampingFactor;
                                        car.AdvancedCouplerDynamicCompressionSlackLimitM = car.CouplerSlackM;
                                    }
                                    else
                                    //                else if (ComputedZone3SlackM > IndividualCouplerSlackM)
                                    {
                                        // Train moving - Allow coupler slack to be slowly increased if it is not the same as the computed value
                                        car.CouplerSlackM = -1.0f * computedZone3SlackM * AdvancedCouplerDuplicationFactor * couplerChangeDampingFactor;
                                        car.CouplerSlackM = MathHelper.Clamp(car.CouplerSlackM, maxZ3CompressionM * AdvancedCouplerDuplicationFactor, 0);
                                        car.AdvancedCouplerDynamicCompressionSlackLimitM = car.CouplerSlackM;
                                    }
                                }
                            }
                            else if (individualCouplerSlackM < maxZ3CompressionM)  // Make sure that a new computed slack value does not take slack into the next zone.
                            {
                                // If computed slack is higher then Zone 3 limit, then set to max Z3. 

                                if (LastCar.SpeedMpS > LastCarCompressionMoveSpeedMpS && LastCar.SpeedMpS <= LastCarZeroSpeedMpS)
                                {
                                    // Train starting - limit slack to maximum
                                    car.CouplerSlackM = maxZ3CompressionM * AdvancedCouplerDuplicationFactor;
                                    car.AdvancedCouplerDynamicCompressionSlackLimitM = car.CouplerSlackM;
                                }
                                else
                                {

                                    // A linear curve is assumed for coupler stiffness - this curve is then used to calculate the amount of slack that the coupler should have. 
                                    //These values are set to "lock" the coupler at this maximum slack length
                                    float slackDiff = Math.Abs(maxZ3CompressionM - maxZ2CompressionM);
                                    float gradStiffness = (car.GetCouplerCompressionStiffness2N() - car.GetCouplerCompressionStiffness1N()) / (slackDiff);
                                    float computedZone4SlackM = (Math.Abs(car.SmoothedCouplerForceUN) / gradStiffness) + Math.Abs(maxZ2CompressionM);

                                    if (computedZone4SlackM < Math.Abs(maxZ3CompressionM) && computedZone4SlackM > Math.Abs(car.PreviousCouplerSlackM) / AdvancedCouplerDuplicationFactor)
                                    {
                                        car.CouplerSlackM = -1.0f * computedZone4SlackM * AdvancedCouplerDuplicationFactor * (1.0f / couplerChangeDampingFactor);
                                        car.CouplerSlackM = MathHelper.Clamp(car.CouplerSlackM, maxZ3CompressionM * AdvancedCouplerDuplicationFactor, 0);
                                        car.AdvancedCouplerDynamicCompressionSlackLimitM = car.CouplerSlackM;
                                    }
                                    else if (computedZone4SlackM > Math.Abs(maxZ3CompressionM))
                                    {
                                        car.CouplerSlackM = maxZ3CompressionM * AdvancedCouplerDuplicationFactor;
                                        car.AdvancedCouplerDynamicCompressionSlackLimitM = car.CouplerSlackM;
                                    }
                                    else if (computedZone4SlackM < Math.Abs(maxZ3CompressionM) && computedZone4SlackM < Math.Abs(car.PreviousCouplerSlackM) / AdvancedCouplerDuplicationFactor)
                                    {
                                        // Decrease coupler slack
                                        car.CouplerSlackM = car.PreviousCouplerSlackM * couplerChangeDampingFactor;
                                        car.AdvancedCouplerDynamicCompressionSlackLimitM = car.CouplerSlackM;
                                    }
                                }
                            }

                            //if (car.SpeedMpS < 0)
                            //Trace.TraceInformation("Zone Compression - ID {0} SmoothForce {1} CouplerForceN {2} Slack {3} Speed {4} Loop {5} ", car.CarID, car.SmoothedCouplerForceUN, car.CouplerForceU, car.CouplerSlackM, car.SpeedMpS, Loop);
                        }
                    }
                }
                else  // Update couplerslack2m which acts as an upper limit in slack calculations for the simple coupler
                {
                    float maxs = car.GetMaximumSimpleCouplerSlack2M();

                    if (car.CouplerForceU > 0) // Compression
                    {
                        float force = -(car.CouplerSlackM + car.GetMaximumSimpleCouplerSlack1M()) * car.GetSimpleCouplerStiffnessNpM();
                        if (car.CouplerSlackM > -maxs && force > car.CouplerForceU)
                            car.CouplerSlack2M = -car.CouplerSlackM;
                        else
                            car.CouplerSlack2M = maxs;
                    }
                    else if (car.CouplerForceU == 0) // Faces not touching
                        car.CouplerSlack2M = maxs;
                    else   // Tension
                    {
                        float force = (car.CouplerSlackM - car.GetMaximumSimpleCouplerSlack1M()) * car.GetSimpleCouplerStiffnessNpM();
                        if (car.CouplerSlackM < maxs && force > car.CouplerForceU)
                            car.CouplerSlack2M = car.CouplerSlackM;
                        else
                            car.CouplerSlack2M = maxs;
                    }
                }
                car.PreviousCouplerSlackM = car.CouplerSlackM;
            }
        }

        /// Update coupler slack - ensures that coupler slack doesn't exceed the maximum permissible value, and provides indication to HUD
        public void UpdateCouplerSlack(double elapsedTime)
        {
            TotalCouplerSlackM = 0;
            CouplersPulled = CouplersPushed = 0;
            for (int i = 0; i < Cars.Count - 1; i++)
            {
                // update coupler slack distance
                TrainCar car = Cars[i];

                // Initialise individual car coupler slack values
                car.RearCouplerSlackM = 0;
                car.FrontCouplerSlackM = 0;

                // Calculate coupler slack - this should be the full amount for both couplers
                car.CouplerSlackM += (float)((car.SpeedMpS - Cars[i + 1].SpeedMpS) * elapsedTime);

                // Make sure that coupler slack does not exceed the maximum (dynamic) coupler slack

                if (car.IsPlayerTrain && !simulator.Settings.SimpleControlPhysics && car.IsAdvancedCoupler) // "Advanced coupler" - operates in three extension zones
                {
                    float advancedCouplerCompressionLimitM = car.AdvancedCouplerDynamicCompressionSlackLimitM;
                    float advancedCouplerTensionLimitM = car.AdvancedCouplerDynamicTensionSlackLimitM;

                    if (car.CouplerSlackM < advancedCouplerCompressionLimitM) // Compression
                        car.CouplerSlackM = advancedCouplerCompressionLimitM;

                    else if (car.CouplerSlackM > advancedCouplerTensionLimitM) // Tension
                        car.CouplerSlackM = advancedCouplerTensionLimitM;
                }
                else // Simple coupler
                {
                    float max = car.GetMaximumSimpleCouplerSlack2M();
                    if (car.CouplerSlackM < -max)  // Compression
                        car.CouplerSlackM = -max;
                    else if (car.CouplerSlackM > max) // Tension
                        car.CouplerSlackM = max;
                }

                // Proportion coupler slack across front and rear couplers of this car, and the following car
                car.RearCouplerSlackM = car.CouplerSlackM / AdvancedCouplerDuplicationFactor;
                car.FrontCouplerSlackM = Cars[i + 1].CouplerSlackM / AdvancedCouplerDuplicationFactor;

                // Check to see if coupler is opened or closed - only closed or opened couplers have been specified
                // It is assumed that the front coupler on first car will always be opened, and so will coupler on last car. All others on the train will be coupled
                car.FrontCouplerOpen = i == 0 && car.FrontCouplerOpenFitted;

                if (i == Cars.Count - 2)
                {
                    Cars[i + 1].RearCouplerOpen = Cars[i + 1].RearCouplerOpenFitted;
                }
                else
                {
                    car.RearCouplerOpen = false;
                }

                TotalCouplerSlackM += car.CouplerSlackM; // Total coupler slack displayed in HUD only

#if DEBUG_COUPLER_FORCES
                if (car.IsAdvancedCoupler)
                {
                    Trace.TraceInformation("Advanced Coupler - Tension - CarID {0} CouplerSlack {1} Zero {2} MaxSlackZone1 {3} MaxSlackZone2 {4} MaxSlackZone3 {5} Stiffness1 {6} Stiffness2 {7} AdvancedCpl {8} CplSlackA {9} CplSlackB {10}  Rigid {11}",
                    car.CarID, car.CouplerSlackM, car.GetCouplerZeroLengthM(), car.GetMaximumCouplerTensionSlack1M(), car.GetMaximumCouplerTensionSlack2M(), car.GetMaximumCouplerTensionSlack3M(),
                    car.GetCouplerTensionStiffness1N(), car.GetCouplerTensionStiffness2N(), car.IsAdvancedCoupler, car.GetCouplerTensionSlackAM(), car.GetCouplerTensionSlackBM(), car.GetCouplerRigidIndication());

                    Trace.TraceInformation("Advanced Coupler - Compression - CarID {0} CouplerSlack {1} Zero {2} MaxSlackZone1 {3} MaxSlackZone2 {4} MaxSlackZone3 {5} Stiffness1 {6} Stiffness2 {7} AdvancedCpl {8} CplSlackA {9} CplSlackB {10}  Rigid {11}",
                    car.CarID, car.CouplerSlackM, car.GetCouplerZeroLengthM(), car.GetMaximumCouplerCompressionSlack1M(), car.GetMaximumCouplerCompressionSlack2M(), car.GetMaximumCouplerCompressionSlack3M(),
                    car.GetCouplerCompressionStiffness1N(), car.GetCouplerCompressionStiffness2N(), car.IsAdvancedCoupler, car.GetCouplerCompressionSlackAM(), car.GetCouplerCompressionSlackBM(), car.GetCouplerRigidIndication());
                }
                else
                {
                    Trace.TraceInformation("Simple Coupler - CarID {0} CouplerSlack {1} Zero {2} MaxSlackZone1 {3} MaxSlackZone2 {4} Stiffness {5} Rigid {6}",
                    car.CarID, car.CouplerSlackM, car.GetCouplerZeroLengthM(), car.GetMaximumSimpleCouplerSlack1M(), car.GetMaximumSimpleCouplerSlack2M(),
                    car.GetSimpleCouplerStiffnessNpM(), car.GetCouplerRigidIndication());
                }
#endif

                if (!car.GetCouplerRigidIndication()) // Flexible coupling - pulling and pushing value will be equal to slack when couplers faces touch
                {

                    if (car.CouplerSlackM >= 0.001) // Coupler pulling
                    {
                        CouplersPulled++;
                        car.HUDCouplerForceIndication = 1;
                    }
                    else if (car.CouplerSlackM <= -0.001) // Coupler pushing
                    {
                        CouplersPushed++;
                        car.HUDCouplerForceIndication = 2;
                    }
                    else
                    {
                        car.HUDCouplerForceIndication = 0; // Coupler neutral
                    }
                }
                else if (car.GetCouplerRigidIndication()) // Rigid coupling - starts pulling/pushing at a lower value then flexible coupling
                {
                    if (car.CouplerSlackM >= 0.000125) // Coupler pulling
                    {
                        CouplersPulled++;
                        car.HUDCouplerForceIndication = 1;
                    }
                    else if (car.CouplerSlackM <= -0.000125) // Coupler pushing
                    {
                        CouplersPushed++;
                        car.HUDCouplerForceIndication = 2;
                    }
                    else
                    {
                        car.HUDCouplerForceIndication = 0; // Coupler neutral
                    }
                }
            }
            foreach (TrainCar car in Cars)
                car.DistanceM += (float)Math.Abs(car.SpeedMpS * elapsedTime);
        }



    }
}
