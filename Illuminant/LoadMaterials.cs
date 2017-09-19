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
        private void LoadMaterials (DefaultMaterialSet materials, ContentManager content) {
            {
                var dBegin = new[] {
                    MaterialUtil.MakeDelegate(
                        rasterizerState: RasterizerState.CullNone,
                        depthStencilState: SphereLightDepthStencilState
                    )
                };
                Action<DeviceManager>[] dEnd = null;

                materials.Add(IlluminantMaterials.SphereLight = new Material(
                    content.Load<Effect>("SphereLight"), "SphereLight", dBegin, dEnd
                ));

                materials.Add(IlluminantMaterials.DirectionalLight = new Material(
                    content.Load<Effect>("DirectionalLight"), "DirectionalLight", dBegin, dEnd
                ));

                materials.Add(IlluminantMaterials.DistanceFieldExterior = 
                    new Material(
                        content.Load<Effect>("DistanceField"), "Exterior",
                        new[] { MaterialUtil.MakeDelegate(RenderStates.MaxBlendValue) }
                    )
                );

                materials.Add(IlluminantMaterials.DistanceFieldInterior = 
                    new Material(
                        content.Load<Effect>("DistanceField"), "Interior", 
                        new[] { MaterialUtil.MakeDelegate(RenderStates.MaxBlendValue) }
                    )
                );

                materials.Add(IlluminantMaterials.ClearDistanceFieldSlice =
                    materials.GetGeometryMaterial(true, blendState: BlendState.Opaque).Clone()
                );

                IlluminantMaterials.DistanceFunctionTypes = new Render.Material[(int)LightObstructionType.MAX + 1];

                foreach (var i in Enum.GetValues(typeof(LightObstructionType))) {
                    var name = Enum.GetName(typeof(LightObstructionType), i);
                    if (name == "MAX")
                        continue;

                    materials.Add(
                        IlluminantMaterials.DistanceFunctionTypes[(int)i] = 
                        new Material(
                            content.Load<Effect>("DistanceFunction"), name,
                            new[] { MaterialUtil.MakeDelegate(RenderStates.MaxBlendValue) }
                        )
                    );
                }

                materials.Add(IlluminantMaterials.HeightVolume = 
                    new Squared.Render.Material(content.Load<Effect>("GBuffer"), "HeightVolume"));

                materials.Add(IlluminantMaterials.HeightVolumeFace = 
                    new Squared.Render.Material(content.Load<Effect>("GBuffer"), "HeightVolumeFace"));

                materials.Add(IlluminantMaterials.MaskBillboard = 
                    new Squared.Render.Material(content.Load<Effect>("GBufferBitmap"), "MaskBillboard"));

                materials.Add(IlluminantMaterials.GDataBillboard = 
                    new Squared.Render.Material(content.Load<Effect>("GBufferBitmap"), "GDataBillboard"));

                materials.Add(IlluminantMaterials.LightingResolve = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "LightingResolve"));

                materials.Add(IlluminantMaterials.GammaCompressedLightingResolve = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "GammaCompressedLightingResolve"));

                materials.Add(IlluminantMaterials.ToneMappedLightingResolve = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "ToneMappedLightingResolve"));

                materials.Add(IlluminantMaterials.ObjectSurfaces = 
                    new Squared.Render.Material(content.Load<Effect>("VisualizeDistanceField"), "ObjectSurfaces"));

                materials.Add(IlluminantMaterials.ObjectOutlines = 
                    new Squared.Render.Material(content.Load<Effect>("VisualizeDistanceField"), "ObjectOutlines"));

                materials.Add(IlluminantMaterials.FunctionSurface = 
                    new Squared.Render.Material(content.Load<Effect>("VisualizeDistanceFunction"), "FunctionSurface"));

                materials.Add(IlluminantMaterials.FunctionOutline = 
                    new Squared.Render.Material(content.Load<Effect>("VisualizeDistanceFunction"), "FunctionOutline"));
            }

            materials.Add(IlluminantMaterials.ScreenSpaceGammaCompressedBitmap = new Squared.Render.Material(
                content.Load<Effect>("HDRBitmap"), "ScreenSpaceGammaCompressedBitmap"
            ));

            materials.Add(IlluminantMaterials.WorldSpaceGammaCompressedBitmap = new Squared.Render.Material(
                content.Load<Effect>("HDRBitmap"), "WorldSpaceGammaCompressedBitmap"
            ));

            materials.Add(IlluminantMaterials.ScreenSpaceToneMappedBitmap = new Squared.Render.Material(
                content.Load<Effect>("HDRBitmap"), "ScreenSpaceToneMappedBitmap"
            ));

            materials.Add(IlluminantMaterials.WorldSpaceToneMappedBitmap = new Squared.Render.Material(
                content.Load<Effect>("HDRBitmap"), "WorldSpaceToneMappedBitmap"
            ));

            materials.PreallocateBindings();

            materials.ForEachMaterial<object>((m, _) => {
                materials.GetUniformBinding<Uniforms.Environment>(m, "Environment");
                materials.GetUniformBinding<Uniforms.DistanceField>(m, "DistanceField");
            }, null);
        }
    }
}
