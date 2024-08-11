using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;
using Squared.Render.Resources;

namespace Squared.Illuminant {
    public partial class IlluminantMaterials : IDisposable {
        public readonly DefaultMaterialSet MaterialSet;

        public Material SphereLight, DirectionalLight, ParticleSystemSphereLight, LineLight, ProjectorLight, VolumetricLight, ShadowedVolumetricLight;
        public Material SphereLightWithDistanceRamp, DirectionalLightWithRamp;
        public Material SphereLightProbe, DirectionalLightProbe, LineLightProbe, ProjectorLightProbe, VolumetricLightProbe;
        public Material SphereLightProbeWithDistanceRamp, 
            DirectionalLightProbeWithRamp;
        public Material SphereLightWithoutDistanceField, DirectionalLightWithoutDistanceField,
            ParticleSystemSphereLightWithoutDistanceField, ProjectorLightWithoutDistanceField;
        public Material DistanceToPolygon;
        public Material[] DistanceFunctionTypes;
        public Material ClearDistanceFieldSlice;
        public Material HeightVolume, HeightVolumeFace, GroundPlane;
        public Material MaskBillboard, GDataBillboard, 
            NormalBillboard, DistanceBillboard;
        public Material ScreenSpaceLightingResolve, 
            ScreenSpaceGammaCompressedLightingResolve, 
            ScreenSpaceToneMappedLightingResolve;
        public Material ScreenSpaceLightingResolveWithAlbedo, 
            ScreenSpaceGammaCompressedLightingResolveWithAlbedo, 
            ScreenSpaceToneMappedLightingResolveWithAlbedo,
            ScreenSpaceLUTBlendedLightingResolveWithAlbedo;
        public Material WorldSpaceLightingResolve, 
            WorldSpaceGammaCompressedLightingResolve, 
            WorldSpaceToneMappedLightingResolve;
        public Material WorldSpaceLightingResolveWithAlbedo, 
            WorldSpaceGammaCompressedLightingResolveWithAlbedo, 
            WorldSpaceToneMappedLightingResolveWithAlbedo,
            WorldSpaceLUTBlendedLightingResolveWithAlbedo;
        public Material ScreenSpaceGammaCompressedBitmap, WorldSpaceGammaCompressedBitmap;
        public Material ScreenSpaceToneMappedBitmap, WorldSpaceToneMappedBitmap;
        public Material AutoGBufferBitmap;
        public Material GBufferMask;
        public Material ObjectSurfaces, ObjectOutlines;
        public Material FunctionSurface, FunctionOutline;
        public Material CalculateLuminance;
        public Material ScreenSpaceVectorWarp, ScreenSpaceNormalRefraction, ScreenSpaceHeightmapRefraction;
        public Material HeightmapToNormals, HeightmapToDisplacement, HeightFromDistance;
        public Material NormalsFromLightmaps;
        private EffectProvider OwnedEffects;

        public bool IsLoaded { get; internal set; }

        internal readonly Material[] MaterialsToSetGammaCompressionParametersOn;
        internal readonly Material[] MaterialsToSetToneMappingParametersOn;

        internal IlluminantMaterials (DefaultMaterialSet materialSet) 
            : base () {
            MaterialSet = materialSet;

            MaterialsToSetGammaCompressionParametersOn = new Material[6];
            MaterialsToSetToneMappingParametersOn = new Material[10];
        }

        public IlluminantMaterials (RenderCoordinator coordinator, DefaultMaterialSet materialSet) 
            : this (materialSet) 
        {
            OwnedEffects = new EffectProvider(System.Reflection.Assembly.GetExecutingAssembly(), coordinator);
            Load(coordinator, OwnedEffects);
        }

        public void Dispose () {
            OwnedEffects?.Dispose();
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

            MaterialsToSetGammaCompressionParametersOn[0] = ScreenSpaceGammaCompressedBitmap;
            MaterialsToSetGammaCompressionParametersOn[1] = WorldSpaceGammaCompressedBitmap;
            MaterialsToSetGammaCompressionParametersOn[2] = ScreenSpaceGammaCompressedLightingResolve;
            MaterialsToSetGammaCompressionParametersOn[3] = ScreenSpaceGammaCompressedLightingResolveWithAlbedo;
            MaterialsToSetGammaCompressionParametersOn[4] = WorldSpaceGammaCompressedLightingResolve;
            MaterialsToSetGammaCompressionParametersOn[5] = WorldSpaceGammaCompressedLightingResolveWithAlbedo;

            foreach (var effect in MaterialsToSetGammaCompressionParametersOn) {
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

            MaterialsToSetToneMappingParametersOn[0] = ScreenSpaceToneMappedBitmap;
            MaterialsToSetToneMappingParametersOn[1] = WorldSpaceToneMappedBitmap;
            MaterialsToSetToneMappingParametersOn[2] = ScreenSpaceToneMappedLightingResolve;
            MaterialsToSetToneMappingParametersOn[3] = ScreenSpaceLightingResolve;
            MaterialsToSetToneMappingParametersOn[4] = ScreenSpaceToneMappedLightingResolveWithAlbedo;
            MaterialsToSetToneMappingParametersOn[5] = ScreenSpaceLightingResolveWithAlbedo;
            MaterialsToSetToneMappingParametersOn[6] = WorldSpaceToneMappedLightingResolve;
            MaterialsToSetToneMappingParametersOn[7] = WorldSpaceLightingResolve;
            MaterialsToSetToneMappingParametersOn[8] = WorldSpaceToneMappedLightingResolveWithAlbedo;
            MaterialsToSetToneMappingParametersOn[9] = WorldSpaceLightingResolveWithAlbedo;

            foreach (var effect in MaterialsToSetToneMappingParametersOn) {
                effect.Parameters["Offset"].SetValue(offset);
                effect.Parameters["ExposureMinusOne"].SetValue(exposure - 1);
                effect.Parameters["GammaMinusOne"].SetValue(gamma - 1);
                var wp = effect.Parameters["WhitePoint"];
                if (wp != null)
                    wp.SetValue(whitePoint);
            }
        }

        public static void SetLUTBlending (Material m, LUTBlendingConfiguration c) {
            var p = m.Parameters;
            p["DarkLUT"].SetValue(c.DarkLUT.Texture);
            p["BrightLUT"].SetValue(c.BrightLUT.Texture);
            p["LUTResolutionsAndRowCounts"].SetValue(new Vector4(c.DarkLUT.Resolution, c.BrightLUT.Resolution, c.DarkLUT.RowCount, c.BrightLUT.RowCount));
            p["LUTLevels"].SetValue(new Vector3(c.DarkLevel, c.NeutralBandSize, c.BrightLevel));
            p["PerChannelLUT"].SetValue(c.PerChannel ? 1f : 0f);
            p["LUTOnly"].SetValue(c.LUTOnly ? 1f : 0f);
            // FIXME: RowIndex
        }
    }
}
