using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Evil;
using Squared.Render.Tracing;

namespace Squared.Illuminant {
    public sealed partial class LightingRenderer : IDisposable, INameableGraphicsObject {
        private void DefineMaterial (Material m) {
            Materials.Add(m);
        }

        private Effect LoadEffect (ContentManager content, string name) {
            return content.Load<Effect>(name);
        }

        private void LoadMaterials (ContentManager content) {
            lock (IlluminantMaterials) {
                if (IlluminantMaterials.IsLoaded)
                    return;

                var dBegin = new[] {
                    MaterialUtil.MakeDelegate(
                        depthStencilState: NeutralDepthStencilState
                    )
                };
                Action<DeviceManager>[] dEnd = null;

                DefineMaterial(IlluminantMaterials.SphereLight = new Material(
                    LoadEffect(content, "SphereLight"), "SphereLight", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.DirectionalLight = new Material(
                    LoadEffect(content, "DirectionalLight"), "DirectionalLight", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.ParticleSystemSphereLight = new Material(
                    LoadEffect(content, "ParticleLight"), "ParticleLight", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.LineLight = new Material(
                    LoadEffect(content, "LineLight"), "LineLight", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.SphereLightWithDistanceRamp = new Material(
                    LoadEffect(content, "SphereLight"), "SphereLightWithDistanceRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.SphereLightWithOpacityRamp = new Material(
                    LoadEffect(content, "SphereLight"), "SphereLightWithOpacityRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.DirectionalLightWithRamp = new Material(
                    LoadEffect(content, "DirectionalLight"), "DirectionalLightWithRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.LineLightWithDistanceRamp = new Material(
                    LoadEffect(content, "LineLight"), "LineLightWithDistanceRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.LineLightWithOpacityRamp = new Material(
                    LoadEffect(content, "LineLight"), "LineLightWithOpacityRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.SphereLightProbe = new Material(
                    LoadEffect(content, "SphereLightProbe"), "SphereLightProbe", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.SphereLightProbeWithDistanceRamp = new Material(
                    LoadEffect(content, "SphereLightProbe"), "SphereLightProbeWithDistanceRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.SphereLightProbeWithOpacityRamp = new Material(
                    LoadEffect(content, "SphereLightProbe"), "SphereLightProbeWithOpacityRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.DirectionalLightProbe = new Material(
                    LoadEffect(content, "DirectionalLight"), "DirectionalLightProbe", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.DirectionalLightProbeWithRamp = new Material(
                    LoadEffect(content, "DirectionalLight"), "DirectionalLightProbeWithRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.LineLightProbe = new Material(
                    LoadEffect(content, "LineLightProbe"), "LineLightProbe", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.LineLightProbeWithDistanceRamp = new Material(
                    LoadEffect(content, "LineLightProbe"), "LineLightProbeWithDistanceRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.LineLightProbeWithOpacityRamp = new Material(
                    LoadEffect(content, "LineLightProbe"), "LineLightProbeWithOpacityRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.GIProbeSelector = new Material(
                    LoadEffect(content, "GIProbe"), "ProbeSelector", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.GIProbeSHGenerator = new Material(
                    LoadEffect(content, "GIProbe"), "SHGenerator", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.VisualizeGI = new Material(
                    LoadEffect(content, "GI"), "VisualizeGI", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.RenderGI = new Material(
                    LoadEffect(content, "GI"), "RenderGI", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.RenderLightProbesFromGI = new Material(
                    LoadEffect(content, "GI"), "RenderLightProbesFromGI", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.DistanceToPolygon = 
                    new Material(
                        LoadEffect(content, "DistanceField"), "DistanceToPolygon",
                        new[] { MaterialUtil.MakeDelegate(RenderStates.MaxBlendValue) }
                    )
                );

                DefineMaterial(IlluminantMaterials.ClearDistanceFieldSlice =
                    new Material(
                        LoadEffect(content, "ClearDistanceField"), "Clear",
                        new[] { MaterialUtil.MakeDelegate(BlendState.Opaque) }
                    )
                );

                IlluminantMaterials.DistanceFunctionTypes = new Render.Material[(int)LightObstructionType.MAX + 1];

                foreach (var i in Enum.GetValues(typeof(LightObstructionType))) {
                    var name = Enum.GetName(typeof(LightObstructionType), i);
                    if (name == "MAX")
                        continue;

                    DefineMaterial(
                        IlluminantMaterials.DistanceFunctionTypes[(int)i] = 
                        new Material(
                            LoadEffect(content, "DistanceFunction"), name,
                            new[] { MaterialUtil.MakeDelegate(RenderStates.MaxBlendValue) }
                        )
                    );
                }

                DefineMaterial(IlluminantMaterials.GroundPlane = 
                    new Squared.Render.Material(LoadEffect(content, "GBuffer"), "GroundPlane"));

                DefineMaterial(IlluminantMaterials.HeightVolume = 
                    new Squared.Render.Material(LoadEffect(content, "GBuffer"), "HeightVolume"));

                DefineMaterial(IlluminantMaterials.HeightVolumeFace = 
                    new Squared.Render.Material(LoadEffect(content, "GBuffer"), "HeightVolumeFace"));

                DefineMaterial(IlluminantMaterials.MaskBillboard = 
                    new Squared.Render.Material(LoadEffect(content, "GBufferBitmap"), "MaskBillboard"));

                DefineMaterial(IlluminantMaterials.GDataBillboard = 
                    new Squared.Render.Material(LoadEffect(content, "GBufferBitmap"), "GDataBillboard"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceLightingResolve = 
                    new Squared.Render.Material(LoadEffect(content, "Resolve"), "ScreenSpaceLightingResolve"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceGammaCompressedLightingResolve = 
                    new Squared.Render.Material(LoadEffect(content, "Resolve"), "ScreenSpaceGammaCompressedLightingResolve"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceToneMappedLightingResolve = 
                    new Squared.Render.Material(LoadEffect(content, "Resolve"), "ScreenSpaceToneMappedLightingResolve"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceLightingResolveWithAlbedo = 
                    new Squared.Render.Material(LoadEffect(content, "Resolve"), "ScreenSpaceLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceGammaCompressedLightingResolveWithAlbedo = 
                    new Squared.Render.Material(LoadEffect(content, "Resolve"), "ScreenSpaceGammaCompressedLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceToneMappedLightingResolveWithAlbedo = 
                    new Squared.Render.Material(LoadEffect(content, "Resolve"), "ScreenSpaceToneMappedLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceLUTBlendedLightingResolveWithAlbedo = 
                    new Squared.Render.Material(LoadEffect(content, "LUTResolve"), "ScreenSpaceLUTBlendedLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.WorldSpaceLightingResolve = 
                    new Squared.Render.Material(LoadEffect(content, "Resolve"), "WorldSpaceLightingResolve"));

                DefineMaterial(IlluminantMaterials.WorldSpaceGammaCompressedLightingResolve = 
                    new Squared.Render.Material(LoadEffect(content, "Resolve"), "WorldSpaceGammaCompressedLightingResolve"));

                DefineMaterial(IlluminantMaterials.WorldSpaceToneMappedLightingResolve = 
                    new Squared.Render.Material(LoadEffect(content, "Resolve"), "WorldSpaceToneMappedLightingResolve"));

                DefineMaterial(IlluminantMaterials.WorldSpaceLightingResolveWithAlbedo = 
                    new Squared.Render.Material(LoadEffect(content, "Resolve"), "WorldSpaceLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.WorldSpaceGammaCompressedLightingResolveWithAlbedo = 
                    new Squared.Render.Material(LoadEffect(content, "Resolve"), "WorldSpaceGammaCompressedLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.WorldSpaceToneMappedLightingResolveWithAlbedo = 
                    new Squared.Render.Material(LoadEffect(content, "Resolve"), "WorldSpaceToneMappedLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.WorldSpaceLUTBlendedLightingResolveWithAlbedo = 
                    new Squared.Render.Material(LoadEffect(content, "LUTResolve"), "WorldSpaceLUTBlendedLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.CalculateLuminance = 
                    new Squared.Render.Material(LoadEffect(content, "Resolve"), "CalculateLuminance"));

                DefineMaterial(IlluminantMaterials.ObjectSurfaces = 
                    new Squared.Render.Material(LoadEffect(content, "VisualizeDistanceField"), "ObjectSurfaces"));

                DefineMaterial(IlluminantMaterials.ObjectOutlines = 
                    new Squared.Render.Material(LoadEffect(content, "VisualizeDistanceField"), "ObjectOutlines"));

                DefineMaterial(IlluminantMaterials.FunctionSurface = 
                    new Squared.Render.Material(LoadEffect(content, "VisualizeDistanceFunction"), "FunctionSurface"));

                DefineMaterial(IlluminantMaterials.FunctionOutline = 
                    new Squared.Render.Material(LoadEffect(content, "VisualizeDistanceFunction"), "FunctionOutline"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceGammaCompressedBitmap = new Squared.Render.Material(
                    LoadEffect(content, "HDRBitmap"), "ScreenSpaceGammaCompressedBitmap"
                ));

                DefineMaterial(IlluminantMaterials.WorldSpaceGammaCompressedBitmap = new Squared.Render.Material(
                    LoadEffect(content, "HDRBitmap"), "WorldSpaceGammaCompressedBitmap"
                ));

                DefineMaterial(IlluminantMaterials.ScreenSpaceToneMappedBitmap = new Squared.Render.Material(
                    LoadEffect(content, "HDRBitmap"), "ScreenSpaceToneMappedBitmap"
                ));

                DefineMaterial(IlluminantMaterials.WorldSpaceToneMappedBitmap = new Squared.Render.Material(
                    LoadEffect(content, "HDRBitmap"), "WorldSpaceToneMappedBitmap"
                ));

                DefineMaterial(IlluminantMaterials.ScreenSpaceVectorWarp = 
                    new Squared.Render.Material(
                        LoadEffect(content, "VectorWarp"), "ScreenSpaceVectorWarp", 
                        new [] { MaterialUtil.MakeDelegate(BlendState.AlphaBlend) }
                    ));

                Materials.PreallocateBindings();

                Materials.ForEachMaterial<object>((m, _) => {
                    Materials.GetUniformBinding<Uniforms.Environment>(m, "Environment");
                    Materials.GetUniformBinding<Uniforms.DistanceField>(m, "DistanceField");
                }, null);

                IlluminantMaterials.IsLoaded = true;
            }
        }
    }
}

namespace Squared.Illuminant.Particles {
    public sealed partial class ParticleEngine : IDisposable {
        private void DefineMaterial (Material m) {
            Materials.Add(m);
        }

        private unsafe Effect LoadEffectFXC (GraphicsDevice device, string path) {
            var bytes = File.ReadAllBytes(path);
            var pDev = GraphicsDeviceUtils.GetIDirect3DDevice9(device);
            void* pEffect, pTemp;
            fixed (byte* pBytes = bytes) {
                var hr = EffectUtils.D3DXCreateEffectEx(pDev, pBytes, (uint)bytes.Length, null, null, "", 0, null, out pEffect, out pTemp);
                if (hr != 0)
                    throw Marshal.GetExceptionForHR(hr);
            }
            var t = typeof(Effect);
            var ctors = t.GetConstructors(
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
            );
            var ctor = ctors[0];
            var result = ctor.Invoke(new object[] { new IntPtr(pEffect), device });
            return (Effect)result;
        }

        private Effect LoadEffect (ContentManager content, string name) {
            var probeDir = @"E:\Documents\Projects\Illuminant\Illuminant\IlluminantContent\bin\x86\Debug\shaders";
            var probePath = Path.Combine(probeDir, name + ".fx.bin");
            if (File.Exists(probePath)) {
                var gds = (IGraphicsDeviceService)content.ServiceProvider.GetService(typeof(IGraphicsDeviceService));
                var device = gds.GraphicsDevice;
                return LoadEffectFXC(device, probePath);
            }
            return content.Load<Effect>(name);
        }

        private void LoadMaterials (ContentManager content) {
            lock (ParticleMaterials) {
                if (ParticleMaterials.IsLoaded)
                    return;

                var dBegin = new[] {
                    MaterialUtil.MakeDelegate(
                        rasterizerState: RasterizerState.CullNone,
                        depthStencilState: DepthStencilState.None,
                        blendState: BlendState.Opaque
                    )
                };
                Action<DeviceManager>[] dEnd = null;

                DefineMaterial(ParticleMaterials.UpdatePositions = new Material(
                    LoadEffect(content, "UpdateParticleSystem"), "UpdatePositions", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.UpdateWithDistanceField = new Material(
                    LoadEffect(content, "UpdateParticleSystemWithDistanceField"), "UpdateWithDistanceField", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.FMA = new Material(
                    LoadEffect(content, "FMA"), "FMA", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.MatrixMultiply = new Material(
                    LoadEffect(content, "MatrixMultiply"), "MatrixMultiply", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.Gravity = new Material(
                    LoadEffect(content, "Gravity"), "Gravity", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.Spawn = new Material(
                    LoadEffect(content, "SpawnParticles"), "SpawnParticles", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.NullTransform = new Material(
                    LoadEffect(content, "NullTransform"), "NullTransform", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.White = new Material(
                    LoadEffect(content, "RasterizeParticleSystem"), "White"
                ));
                DefineMaterial(ParticleMaterials.AttributeColor = new Material(
                    LoadEffect(content, "RasterizeParticleSystem"), "AttributeColor"
                ));
                DefineMaterial(ParticleMaterials.WhiteNoTexture = new Material(
                    LoadEffect(content, "RasterizeParticleSystem"), "WhiteNoTexture"
                ));
                DefineMaterial(ParticleMaterials.AttributeColorNoTexture = new Material(
                    LoadEffect(content, "RasterizeParticleSystem"), "AttributeColorNoTexture"
                ));

                Materials.ForEachMaterial<object>((m, _) => {
                    Materials.GetUniformBinding<Uniforms.Environment>(m, "Environment");
                    Materials.GetUniformBinding<Uniforms.DistanceField>(m, "DistanceField");
                    Materials.GetUniformBinding<Uniforms.ParticleSystem>(m, "System");
                }, null);

                Materials.PreallocateBindings();

                ParticleMaterials.IsLoaded = true;
            }
        }
    }
}
