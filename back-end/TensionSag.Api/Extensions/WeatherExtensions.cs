using TensionSag.Api.Models;
using System;

namespace TensionSag.Api.Extensions
{
    public static class WeatherExtensions
    {
        //this contains all of the calculations that are dependent on the weather condition and loading of the wire
        //includes sag 
        private static readonly double IceDensity = 916.8;
        private static readonly double Gravity = 9.80665;

        //this calculates the final elastic tension
        //the calculation assumes all plastic elongation has occured prior to the weather condition 
        //long term plastic elongation (creep) is assumed to be the controlling plastic elongation
        //future work needs to be done to calculate the maximum plastic strain due to high tension and then force this calculation to use the higher of the two.
        public static double CalculateElasticTension(this Weather weather, Wire wire, Creep creep)
        {
            double orginalLength = WireExtensions.CalculateOriginalLength(wire, creep);
            double StartingWireLength = orginalLength + orginalLength * CreepExtensions.CalculateCreepStrain(creep, wire);
            double StartingWireLengthDesignTemp = StartingWireLength + WireExtensions.CalculateWireThermalCoefficient(wire) * StartingWireLength * (weather.Temperature - wire.StartingTemp);
            double psi = StartingWireLength + WireExtensions.CalculateWireThermalCoefficient(wire) * StartingWireLength * (weather.Temperature - wire.StartingTemp);
            double beta = StartingWireLength / (WireExtensions.CalculateWireElasticity(wire) * wire.TotalCrossSection);

            double lengthEstimate = Math.Sqrt(Math.Pow(weather.FinalSpanLength, 2) + Math.Pow(weather.FinalElevation, 2));
            double horizontalTension = CalculateFinalLinearForce(weather, wire) * (lengthEstimate * lengthEstimate) /( 8 * Math.Sqrt(3*lengthEstimate*(Math.Abs(StartingWireLengthDesignTemp - lengthEstimate)) / 8 ));

            double difference = 100;
            while (Math.Abs(difference) > 0.001d)
            {
                difference = SolveForDifference(horizontalTension, weather.FinalSpanLength, CalculateFinalLinearForce(weather, wire), weather.FinalElevation, psi, beta);
                horizontalTension = (horizontalTension - difference);

            }

            return horizontalTension;
        }

        //this calculates the 'initial' tension from the initial stress strain curve, assumption is that no plastic elongation has occured yet, but the wire does not experience linear elasticity.
        //this tension is typically called the 1-hour creep tension,'short' term tension condition, or stringing tensions.
        //vaguely this calculation goes like this: 1)estimate length at design case 2) calculate strain for that length 3) calculate stress for that strain 4) calculate the average tension for that stress
        //5) find the horizontal tension that results in that average tension 6) calculate the wire length for that horizontal tension from the wire geometry and return to step 2) with the new wire length estimate
        //in practice this is accomplished by plugging stress= h/rho and strain = (arclength- originallength)/originallength into the stress strain equation
        //this results in an equation that has only one unknown, horizontal tension. this can be solved with a newton-raphson loop.
        //our input stress strain equation is in % strain, so the engineering strain must be multiplied by 100
        public static double CalculateInitialTensions(this Weather weather, Wire wire, Creep creep)
        {
            
            double originalLength = wire.CalculateOriginalLength(creep);
            double originalLengthDesignTemp = originalLength + WireExtensions.CalculateWireThermalCoefficient(wire) * originalLength * (weather.Temperature - wire.StartingTemp);

            double lengthEstimate = Math.Sqrt(Math.Pow(weather.FinalSpanLength, 2) + Math.Pow(weather.FinalElevation, 2));
            double horizontalTension = CalculateFinalLinearForce(weather, wire) * (lengthEstimate * lengthEstimate) / (8 * Math.Sqrt(3 * lengthEstimate * (Math.Abs(originalLengthDesignTemp - lengthEstimate)) / 8));

            //refactor this so wireStressStrains are not calculated both here and in the wireExtensions
            double wireStressStrainK0 = wire.OuterStressStrainList[0] + wire.CoreStressStrainList[0];
            double wireStressStrainK1 = wire.OuterStressStrainList[1] + wire.CoreStressStrainList[1];
            double wireStressStrainK2 = wire.OuterStressStrainList[2] + wire.CoreStressStrainList[2];
            double wireStressStrainK3 = wire.OuterStressStrainList[3] + wire.CoreStressStrainList[3];
            double wireStressStrainK4 = wire.OuterStressStrainList[4] + wire.CoreStressStrainList[4];

            double NewtonDiff = 1000;
            while (Math.Abs(NewtonDiff) > 0.001d)
            {
                //standard newton raphson where f(x)=stress-strain equation with x (strain) substituted for (arclength-oglength)/oglength*100. newton raphson to solve for horizontal tension
                double arcLength = CalculateArcLength(weather.FinalSpanLength, weather.FinalElevation, horizontalTension / CalculateFinalLinearForce(weather, wire));
                double arcLengthPrime = CalculateArcLengthPrime(horizontalTension, weather.FinalSpanLength, CalculateFinalLinearForce(weather, wire), weather.FinalElevation);

                double function = -horizontalTension / wire.TotalCrossSection + wireStressStrainK0 + wireStressStrainK1 * (arcLength / originalLengthDesignTemp - 1) * 100 +
                    wireStressStrainK2 * (10000 * Math.Pow(arcLength, 2) / Math.Pow(originalLengthDesignTemp, 2) - 20000 * arcLength / originalLengthDesignTemp + 10000) +
                    wireStressStrainK3 * (1000000 * Math.Pow(arcLength, 3) / Math.Pow(originalLengthDesignTemp, 3) - 3000000 * Math.Pow(arcLength, 2) / Math.Pow(originalLengthDesignTemp, 2) + 3000000 * arcLength / originalLengthDesignTemp - 1000000) +
                    wireStressStrainK4 * (100000000 * Math.Pow(arcLength, 4) / Math.Pow(originalLengthDesignTemp, 4) - 400000000 * Math.Pow(arcLength, 3) / Math.Pow(originalLengthDesignTemp, 3) + 600000000 * Math.Pow(arcLength, 2) / Math.Pow(originalLengthDesignTemp, 2) - 400000000 * arcLength / originalLengthDesignTemp + 100000000);

                double functionPrime = -1/wire.TotalCrossSection + wireStressStrainK1 * arcLengthPrime/originalLengthDesignTemp*100 + 
                    wireStressStrainK2 * (10000 * 2 * arcLength * arcLengthPrime / Math.Pow(originalLengthDesignTemp, 2) - 20000 * arcLengthPrime / originalLengthDesignTemp ) +
                    wireStressStrainK3 * (1000000 * 3 * Math.Pow(arcLength, 2) * arcLengthPrime / Math.Pow(originalLengthDesignTemp, 3) - 3000000 * 2 * arcLength * arcLengthPrime / Math.Pow(originalLengthDesignTemp, 2) + 3000000 * arcLengthPrime / originalLengthDesignTemp ) +
                    wireStressStrainK4 * (100000000 * 4 * Math.Pow(arcLength, 3) * arcLengthPrime / Math.Pow(originalLengthDesignTemp, 4) - 400000000 * 3 * Math.Pow(arcLength, 2) * arcLengthPrime / Math.Pow(originalLengthDesignTemp, 3) + 600000000 * 2 * arcLength * arcLengthPrime / Math.Pow(originalLengthDesignTemp, 2) - 400000000 * arcLengthPrime / originalLengthDesignTemp );

                NewtonDiff = function / functionPrime;
                horizontalTension = horizontalTension - NewtonDiff;

            }

            return horizontalTension;
        }

        //calculates sag for any wire geometry. sag is defined as the largest separation between the wire geometry and the straight line between attachment points.
        public static double CalculateSag(double catenaryConstant, double spanLength, double spanElevation)
        {
            double XcForSag = CalculateXc(spanLength, spanElevation, catenaryConstant);
            double YcForSag = CalculateYc(catenaryConstant, XcForSag);
            double distanceToSagPoint = CalculateXd(XcForSag, catenaryConstant, spanElevation, spanLength);

            double tempSagXc = (spanElevation / spanLength) * distanceToSagPoint;
            double tempSagYc = YcForSag + catenaryConstant * (Math.Sqrt(Math.Pow(spanLength, 2) + Math.Pow(spanElevation, 2)) - spanLength) / spanLength;

            double sag = tempSagXc - tempSagYc;

            return sag;
        }

        //this is the y coordinate of the lowest point in the catenary curve. it should always be negative but for uplift conditions maybe behind the current structure.
        public static double CalculateYc(double catenaryConstant, double Xc)
        {
            return -catenaryConstant * (MathUtility.Cosh(-Xc / catenaryConstant) - 1);
        }

        //this is the distance to the sag point, notably not the same as Xc for spans with uneven elevations.
        public static double CalculateXd(double Xc, double catenaryConstant, double spanElevation, double spanLength)
        {
            return Xc + catenaryConstant * MathUtility.Asinh(spanElevation / spanLength);
        }

        //this is the x coordinate for the lowest point in the catenary geometry of the wire. for uplift conditions it is negative.
        public static double CalculateXc(double spanLength, double spanElevation, double catenaryConstant)
        {
            double tempZVar = spanElevation * Math.Sqrt(Math.Exp(spanLength / catenaryConstant)) / (catenaryConstant * (1 - Math.Exp(spanLength / catenaryConstant)));
            return spanLength / 2.0 + catenaryConstant * Math.Log(tempZVar + Math.Sqrt(1 + tempZVar * tempZVar));
        }

        //calculates the total hanging wire length between attachment points
        public static double CalculateArcLength(double spanLength, double spanElevation, double catenaryConstant)
        {
            double Xc = CalculateXc(spanLength, spanElevation, catenaryConstant);
            return catenaryConstant * (MathUtility.Sinh((spanLength - Xc) / catenaryConstant) + MathUtility.Sinh(Xc / catenaryConstant));
        }

        public static double CalculateWeightLinearForce(this Weather weather, Wire wire)
        {
            return -((Math.PI * Math.Pow(wire.FinalWireDiameter / 2 + weather.IceRadius, 2d) - (Math.PI * Math.Pow(wire.FinalWireDiameter / 2, 2d))) * IceDensity * Gravity + wire.FinalWireLinearWeight);
        }

        //calculates the final weather loaded linear weight of the wire and bundle. does not account for NESC linear constant yet
        public static double CalculateFinalLinearForce(this Weather weather, Wire wire)
        {
            //this wind linear force calculation assumes wind acts perpendicular to the wire. to account for off axis winds yaw wind must be accounted for.
            double WindLinearForce = (wire.FinalWireDiameter+weather.IceRadius*2) * weather.WindPressure;
            double WeightLinearForce = CalculateWeightLinearForce(weather, wire);

            return Math.Sqrt(Math.Pow(WindLinearForce, 2d) + Math.Pow(WeightLinearForce, 2d));
        }

        //newton raphson method junk for the elastic tension calculation. this basically follows the numerical tension method but very accurately accounts for changes in elevation
        public static double SolveForDifference(double horizontalTension, double finalSpanLength, double linearForce, double finalSpanElevation, double psi, double beta)
        {
            double arcLengthPrime = CalculateArcLengthPrime(horizontalTension, finalSpanLength, linearForce, finalSpanElevation);

            double arcLength = CalculateArcLength(finalSpanLength, finalSpanElevation, (horizontalTension / linearForce));

            return (psi + horizontalTension * beta - arcLength) / (beta - (arcLengthPrime));

        }

        //this is the derivative of the arc length calculation with respect to horizontal tension
        //used in the newton raphson cycles for both 'initial' (stringing) and 'final' (post full creep or 10-year creep) tensions.
        public static double CalculateArcLengthPrime(double horizontalTension, double finalSpanLength, double linearForce, double finalSpanElevation)
        {
            double iota = Math.Exp(finalSpanLength * linearForce / horizontalTension);
            double kappa = Math.Pow(iota, 1.5d) * finalSpanElevation * finalSpanLength * Math.Pow(linearForce, 2d) / (Math.Pow(1d - iota, 2d) * Math.Pow(horizontalTension, 3d));
            double eta = Math.Sqrt(iota) * finalSpanElevation * finalSpanLength * Math.Pow(linearForce, 2d) / (2d * (1d - iota) * Math.Pow(horizontalTension, 3d));
            double mu = Math.Sqrt(iota) * finalSpanElevation * linearForce / ((1d - iota) * Math.Pow(horizontalTension, 2d));
            double nu = linearForce * (Math.Sqrt(1d + (iota * Math.Pow(finalSpanElevation, 2d) * Math.Pow(linearForce, 2d)) / (Math.Pow(1d - iota, 2d) * Math.Pow(horizontalTension, 2d))));
            double xi = MathUtility.Asinh((Math.Sqrt(iota) * finalSpanElevation * linearForce) / ((1d - iota) * horizontalTension)) / linearForce;
            double omikron = linearForce * (-1d * (-kappa - eta - mu) * horizontalTension / nu - xi) / horizontalTension;
            double chi = linearForce * ((-kappa - eta - mu) * horizontalTension / nu + xi) / horizontalTension;

            double tau = (horizontalTension / linearForce) * (omikron - (linearForce / Math.Pow(horizontalTension, 2d)) *
                (finalSpanLength / 2d - horizontalTension * xi)) * MathUtility.Cosh((linearForce *
                (finalSpanLength / 2d - horizontalTension * xi)) / horizontalTension) +
                MathUtility.Sinh((linearForce * (finalSpanLength / 2d - horizontalTension * xi)) / horizontalTension) / linearForce;

            double upsilon = (horizontalTension / linearForce) * (chi - (linearForce / Math.Pow(horizontalTension, 2d)) *
                (finalSpanLength / 2d + horizontalTension * xi)) * MathUtility.Cosh((linearForce *
                (finalSpanLength / 2d + horizontalTension * xi)) / horizontalTension) +
                MathUtility.Sinh((linearForce * (finalSpanLength / 2d + horizontalTension * xi)) / horizontalTension) / linearForce;

            return (tau + upsilon);
        }

        //calculates the vertical force at the current support structure due to the wire in what ever coordinate system it is given
        //typically the wire's coordinate system is used and a transform is done later to find the vertical force transfered to the structure
        //im not exactly sure where these equations are from, or why they result in a different value from simply calculating -Math.Sinh(Xc / catenaryConstant) * horizontalTension
        //these equations are potentually transforming the Xc point in the blown geometry but i cannot find documentation on it at the moment
        public static double CalculateVerticalForce(this Weather weather, Wire wire, double blownSpanLength, double blownElevation, double horizontalTension)
        {
            double catenaryConstant = horizontalTension / CalculateFinalLinearForce(weather, wire);

            //double Xc = CalculateXc(blownSpanLength, blownElevation, catenaryConstant);
            //return -Math.Sinh(Xc / catenaryConstant) * horizontalTension;

            double sn = 2 * catenaryConstant * Math.Sinh(blownSpanLength / (2 * catenaryConstant));
            double s = Math.Sqrt(Math.Pow(sn, 2) + Math.Pow(blownElevation, 2));
            double ub = blownSpanLength / 2 - catenaryConstant * Math.Log((s + blownElevation) / sn);
            double vPrime = horizontalTension * Math.Sinh(ub / catenaryConstant);
            return vPrime;

        }

        //These four 'blown' calculations find the wire geometry when it swings out of the plane of the support structures due to transverse wind forces
        //blown elevation and span length are used for the vertical force calculations
        public static double BlownVerticalAngle(this Weather weather, Wire wire)
        {
            return Math.Acos(-CalculateWeightLinearForce(weather, wire) / CalculateFinalLinearForce(weather, wire));
        }

        public static double BlownHorizontalAngle(this Weather weather, Wire wire, double finalSpanElevation, double finalSpanLength)
        {
            return Math.Atan(-finalSpanElevation * Math.Sin(BlownVerticalAngle(weather, wire)) / finalSpanLength);
        }

        public static double BlownSpanElevation(this Weather weather, Wire wire, double finalSpanElevation)
        {
            return finalSpanElevation * Math.Cos(BlownVerticalAngle(weather, wire));
        }

        public static double BlownSpanLength(this Weather weather, Wire wire, double finalSpanElevation, double finalSpanLength)
        {
            return finalSpanLength / Math.Cos(BlownHorizontalAngle(weather, wire, finalSpanElevation, finalSpanLength));
        }

        //These functions translate the wire forces in the blown/swung out geometry coordinate system back to the support structures coordinate system
        public static double StructureVerticalForce(this Weather weather, Wire wire, double finalSpanElevation, double finalSpanLength, double horizontalTension)
        {
            double blownspanlength = BlownSpanLength(weather, wire, finalSpanElevation, finalSpanLength);
            double blownelevation = BlownSpanElevation(weather, wire, finalSpanElevation);
            double verticalForce = CalculateVerticalForce(weather, wire, blownspanlength, blownelevation, horizontalTension);
            double blownVerticalAngle = BlownVerticalAngle(weather, wire);
            double blownHorizontalAngle = BlownHorizontalAngle(weather, wire, finalSpanElevation, finalSpanLength);

            return -(verticalForce * Math.Cos(blownVerticalAngle) + horizontalTension * Math.Sin(blownHorizontalAngle) * Math.Sin(blownVerticalAngle));
        }

        public static double StructureLongitudinalForce(this Weather weather, Wire wire, double finalSpanElevation, double finalSpanLength, double horizontalTension)
        {
            return horizontalTension * Math.Cos(BlownHorizontalAngle(weather, wire, finalSpanElevation, finalSpanLength));
        }

        //this is the wind force exerted on the structure due to the wire. it acts perpendicular to the wire span and horizontal tension.
        public static double StructureTangentialForce(this Weather weather, Wire wire, double finalSpanElevation, double finalSpanLength, double horizontalTension)
        {
            double blownspanlength = BlownSpanLength(weather, wire, finalSpanElevation, finalSpanLength);
            double blownelevation = BlownSpanElevation(weather, wire, finalSpanElevation);
            double verticalForce = CalculateVerticalForce(weather, wire, blownspanlength, blownelevation, horizontalTension);
            double blownVerticalAngle = BlownVerticalAngle(weather, wire);
            double blownHorizontalAngle = BlownHorizontalAngle(weather, wire, finalSpanElevation, finalSpanLength);

            return verticalForce * Math.Sin(blownVerticalAngle) - horizontalTension * Math.Sin(blownHorizontalAngle) * Math.Cos(blownVerticalAngle);
        }

    }
}