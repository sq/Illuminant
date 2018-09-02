using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;

namespace Squared.Illuminant {
    public class IlluminantMaterials {
        public readonly DefaultMaterialSet MaterialSet;

        public Material SphereLight, DirectionalLight, ParticleSystemSphereLight;
        public Material SphereLightWithDistanceRamp, SphereLightWithOpacityRamp, DirectionalLightWithRamp;
        public Material SphereLightProbe, DirectionalLightProbe;
        public Material SphereLightProbeWithDistanceRamp, SphereLightProbeWithOpacityRamp, DirectionalLightProbeWithRamp;
        public Material DistanceFieldExterior, DistanceFieldInterior;
        public Material[] DistanceFunctionTypes;
        public Material ClearDistanceFieldSlice;
        public Material HeightVolumeTopFace, HeightVolumeFrontFace, GroundPlane;
        public Material MaskBillboard, GDataBillboard;
        public Material LightingResolve, 
            GammaCompressedLightingResolve, ToneMappedLightingResolve;
        public Material ScreenSpaceGammaCompressedBitmap, WorldSpaceGammaCompressedBitmap;
        public Material ScreenSpaceToneMappedBitmap, WorldSpaceToneMappedBitmap;
        public Material ObjectSurfaces, ObjectOutlines, FieldSlice;
        public Material FunctionSurface, FunctionOutline, FunctionSlice;
        public Material CalculateLuminance;
        public Material GIProbeSelector, GIProbeSHGenerator;
        public Material VisualizeGI;
        public Material RenderGI, RenderLightProbesFromGI;

        internal readonly Effect[] EffectsToSetGammaCompressionParametersOn;
        internal readonly Effect[] EffectsToSetToneMappingParametersOn;

        internal IlluminantMaterials (DefaultMaterialSet materialSet) {
            MaterialSet = materialSet;

            EffectsToSetGammaCompressionParametersOn = new Effect[3];
            EffectsToSetToneMappingParametersOn = new Effect[4];
        }

        /// <summary>
        /// Updates the gamma compression parameters for the gamma compressed bitmap materials. You should call this in batch setup when using the materials.
        /// </summary>
        /// <param name="middleGray">I don't know what this does. Impossible to find a paper that actually describes this formula. :/ Try 0.6.</param>
        /// <param name="averageLuminance">The average luminance of the entire scene. You can compute this by scaling the entire scene down or using light receivers.</param>
        /// <param name="maximumLuminance">The maximum luminance. Luminance values above this threshold will remain above 1.0 after gamma compression.</param>
        /// <param name="offset">A constant added to incoming values before exposure is applied.</param>
        public void SetGammaCompressionParameters (float middleGray, float averageLuminance, float maximumLuminance, float offset = 0) {
            const float min = 1 / 256f;
            const float max = 99999f;

            middleGray = MathHelper.Clamp(middleGray, 0.0f, max);
            averageLuminance = MathHelper.Clamp(averageLuminance, min, max);
            maximumLuminance = MathHelper.Clamp(maximumLuminance, min, max);

            EffectsToSetGammaCompressionParametersOn[0] = ScreenSpaceGammaCompressedBitmap.Effect;
            EffectsToSetGammaCompressionParametersOn[1] = WorldSpaceGammaCompressedBitmap.Effect;
            EffectsToSetGammaCompressionParametersOn[2] = GammaCompressedLightingResolve.Effect;

            foreach (var effect in EffectsToSetGammaCompressionParametersOn) {
                effect.Parameters["Offset"].SetValue(offset);
                effect.Parameters["MiddleGray"].SetValue(middleGray);
                effect.Parameters["AverageLuminance"].SetValue(averageLuminance);
                effect.Parameters["MaximumLuminanceSquared"].SetValue(maximumLuminance * maximumLuminance);
            }
        }

        /// <summary>
        /// Updates the tone mapping parameters for the tone mapped bitmap materials. You should call this in batch setup when using the materials.
        /// </summary>
        /// <param name="exposure">A factor to multiply incoming values to make them brighter or darker.</param>
        /// <param name="whitePoint">The white point to set as the threshold above which any values become 1.0.</param>
        /// <param name="offset">A constant added to incoming values before exposure is applied.</param>
        public void SetToneMappingParameters (float exposure, float whitePoint, float offset = 0, float gamma = 1) {
            const float min = 1 / 256f;
            const float max = 99999f;

            exposure = MathHelper.Clamp(exposure, min, max);
            whitePoint = MathHelper.Clamp(whitePoint, min, max);
            gamma = MathHelper.Clamp(gamma, 0.1f, 4.0f);

            EffectsToSetToneMappingParametersOn[0] = ScreenSpaceToneMappedBitmap.Effect;
            EffectsToSetToneMappingParametersOn[1] = WorldSpaceToneMappedBitmap.Effect;
            EffectsToSetToneMappingParametersOn[2] = ToneMappedLightingResolve.Effect;
            EffectsToSetToneMappingParametersOn[3] = LightingResolve.Effect;

            foreach (var effect in EffectsToSetToneMappingParametersOn) {
                effect.Parameters["Offset"].SetValue(offset);
                effect.Parameters["ExposureMinusOne"].SetValue(exposure - 1);
                effect.Parameters["GammaMinusOne"].SetValue(gamma - 1);
                var wp = effect.Parameters["WhitePoint"];
                if (wp != null)
                    wp.SetValue(whitePoint);
            }
        }
    }
}
