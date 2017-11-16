using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;
using Squared.Render.Convenience;

namespace Squared.Illuminant {
    public sealed partial class LightingRenderer : IDisposable {
        private void DefineMaterial (Material m) {
            Materials.Add(m);
        }

        private void LoadMaterials (ContentManager content) {
            {
                var dBegin = new[] {
                    MaterialUtil.MakeDelegate(
                        rasterizerState: RasterizerState.CullNone,
                        depthStencilState: SphereLightDepthStencilState
                    )
                };
                Action<DeviceManager>[] dEnd = null;

                DefineMaterial(IlluminantMaterials.SphereLight = new Material(
                    content.Load<Effect>("SphereLight"), "SphereLight", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.DirectionalLight = new Material(
                    content.Load<Effect>("DirectionalLight"), "DirectionalLight", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.SphereLightWithDistanceRamp = new Material(
                    content.Load<Effect>("SphereLight"), "SphereLightWithDistanceRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.SphereLightWithOpacityRamp = new Material(
                    content.Load<Effect>("SphereLight"), "SphereLightWithOpacityRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.DirectionalLightWithRamp = new Material(
                    content.Load<Effect>("DirectionalLight"), "DirectionalLightWithRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.DistanceFieldExterior = 
                    new Material(
                        content.Load<Effect>("DistanceField"), "Exterior",
                        new[] { MaterialUtil.MakeDelegate(RenderStates.MaxBlendValue) }
                    )
                );

                DefineMaterial(IlluminantMaterials.DistanceFieldInterior = 
                    new Material(
                        content.Load<Effect>("DistanceField"), "Interior", 
                        new[] { MaterialUtil.MakeDelegate(RenderStates.MaxBlendValue) }
                    )
                );

                DefineMaterial(IlluminantMaterials.ClearDistanceFieldSlice =
                    Materials.GetGeometryMaterial(true, blendState: BlendState.Opaque).Clone()
                );

                IlluminantMaterials.DistanceFunctionTypes = new Render.Material[(int)LightObstructionType.MAX + 1];

                foreach (var i in Enum.GetValues(typeof(LightObstructionType))) {
                    var name = Enum.GetName(typeof(LightObstructionType), i);
                    if (name == "MAX")
                        continue;

                    DefineMaterial(
                        IlluminantMaterials.DistanceFunctionTypes[(int)i] = 
                        new Material(
                            content.Load<Effect>("DistanceFunction"), name,
                            new[] { MaterialUtil.MakeDelegate(RenderStates.MaxBlendValue) }
                        )
                    );
                }

                DefineMaterial(IlluminantMaterials.HeightVolume = 
                    new Squared.Render.Material(content.Load<Effect>("GBuffer"), "HeightVolume"));

                DefineMaterial(IlluminantMaterials.HeightVolumeFace = 
                    new Squared.Render.Material(content.Load<Effect>("GBuffer"), "HeightVolumeFace"));

                DefineMaterial(IlluminantMaterials.MaskBillboard = 
                    new Squared.Render.Material(content.Load<Effect>("GBufferBitmap"), "MaskBillboard"));

                DefineMaterial(IlluminantMaterials.GDataBillboard = 
                    new Squared.Render.Material(content.Load<Effect>("GBufferBitmap"), "GDataBillboard"));

                DefineMaterial(IlluminantMaterials.LightingResolve = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "LightingResolve"));

                DefineMaterial(IlluminantMaterials.GammaCompressedLightingResolve = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "GammaCompressedLightingResolve"));

                DefineMaterial(IlluminantMaterials.ToneMappedLightingResolve = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "ToneMappedLightingResolve"));

                DefineMaterial(IlluminantMaterials.ObjectSurfaces = 
                    new Squared.Render.Material(content.Load<Effect>("VisualizeDistanceField"), "ObjectSurfaces"));

                DefineMaterial(IlluminantMaterials.ObjectOutlines = 
                    new Squared.Render.Material(content.Load<Effect>("VisualizeDistanceField"), "ObjectOutlines"));

                DefineMaterial(IlluminantMaterials.FunctionSurface = 
                    new Squared.Render.Material(content.Load<Effect>("VisualizeDistanceFunction"), "FunctionSurface"));

                DefineMaterial(IlluminantMaterials.FunctionOutline = 
                    new Squared.Render.Material(content.Load<Effect>("VisualizeDistanceFunction"), "FunctionOutline"));
            }

            DefineMaterial(IlluminantMaterials.ScreenSpaceGammaCompressedBitmap = new Squared.Render.Material(
                content.Load<Effect>("HDRBitmap"), "ScreenSpaceGammaCompressedBitmap"
            ));

            DefineMaterial(IlluminantMaterials.WorldSpaceGammaCompressedBitmap = new Squared.Render.Material(
                content.Load<Effect>("HDRBitmap"), "WorldSpaceGammaCompressedBitmap"
            ));

            DefineMaterial(IlluminantMaterials.ScreenSpaceToneMappedBitmap = new Squared.Render.Material(
                content.Load<Effect>("HDRBitmap"), "ScreenSpaceToneMappedBitmap"
            ));

            DefineMaterial(IlluminantMaterials.WorldSpaceToneMappedBitmap = new Squared.Render.Material(
                content.Load<Effect>("HDRBitmap"), "WorldSpaceToneMappedBitmap"
            ));

            Materials.PreallocateBindings();

            Materials.ForEachMaterial<object>((m, _) => {
                Materials.GetUniformBinding<Uniforms.Environment>(m, "Environment");
                Materials.GetUniformBinding<Uniforms.DistanceField>(m, "DistanceField");
            }, null);
        }
    }

    public sealed partial class ParticleEngine : IDisposable {
        private void DefineMaterial (Material m) {
            Materials.Add(m);
        }

        private void LoadMaterials (ContentManager content) {
            {
                var dBegin = new[] {
                    MaterialUtil.MakeDelegate(
                        rasterizerState: RasterizerState.CullNone,
                        depthStencilState: DepthStencilState.None,
                        blendState: BlendState.Opaque
                    )
                };
                Action<DeviceManager>[] dEnd = null;

                DefineMaterial(ParticleMaterials.UpdatePositions = new Material(
                    content.Load<Effect>("UpdateParticleSystem"), "UpdatePositions", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.PositionFMA = new Material(
                    content.Load<Effect>("PermuteParticleSystem"), "PositionFMA", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.VelocityFMA = new Material(
                    content.Load<Effect>("PermuteParticleSystem"), "VelocityFMA", dBegin, dEnd
                ));
            }

            DefineMaterial(ParticleMaterials.RasterizeParticles = new Material(
                content.Load<Effect>("RasterizeParticleSystem"), "RasterizeParticles"
            ));

            Materials.PreallocateBindings();
        }
    }
}
