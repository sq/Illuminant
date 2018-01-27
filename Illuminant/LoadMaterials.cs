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

                DefineMaterial(IlluminantMaterials.SphereLightProbe = new Material(
                    content.Load<Effect>("SphereLight"), "SphereLightProbe", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.DirectionalLightProbe = new Material(
                    content.Load<Effect>("DirectionalLight"), "DirectionalLightProbe", dBegin, dEnd
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

                DefineMaterial(IlluminantMaterials.CalculateLuminance = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "CalculateLuminance"));

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

                DefineMaterial(ParticleMaterials.UpdateWithDistanceField = new Material(
                    content.Load<Effect>("UpdateParticleSystemWithDistanceField"), "UpdateWithDistanceField", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.FMA = new Material(
                    content.Load<Effect>("FMA"), "FMA", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.MatrixMultiply = new Material(
                    content.Load<Effect>("MatrixMultiply"), "MatrixMultiply", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.Gravity = new Material(
                    content.Load<Effect>("Gravity"), "Gravity", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.Spawn = new Material(
                    content.Load<Effect>("SpawnParticles"), "SpawnParticles", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.NullTransform = new Material(
                    content.Load<Effect>("NullTransform"), "NullTransform", dBegin, dEnd
                ));
            }

            DefineMaterial(ParticleMaterials.White = new Material(
                content.Load<Effect>("RasterizeParticleSystem"), "White"
            ));
            DefineMaterial(ParticleMaterials.AttributeColor = new Material(
                content.Load<Effect>("RasterizeParticleSystem"), "AttributeColor"
            ));

            Materials.PreallocateBindings();
        }
    }
}
