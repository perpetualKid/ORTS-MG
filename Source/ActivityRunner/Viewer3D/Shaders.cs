// COPYRIGHT 2009, 2010, 2011, 2012, 2013 by the Open Rails project.
// 
// This file is part of Open Rails.
// 
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Open Rails is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with Open Rails.  If not, see <http://www.gnu.org/licenses/>.

// This file is the responsibility of the 3D & Environment Team. 

using System;
using System.IO;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Orts.ActivityRunner.Viewer3D.Processes;
using Orts.ActivityRunner.Viewer3D.Shaders;
using Orts.Common.Xna;
using Orts.Simulation;

namespace Orts.ActivityRunner.Viewer3D
{

    public class SceneryShader : BaseShader
    {
        private readonly EffectParameter world;
        private readonly EffectParameter worldViewProjection;
        private readonly EffectParameter[] lightViewProjectionShadowProjection;
        private readonly EffectParameter[] shadowMapTextures;
        private readonly EffectParameter shadowMapLimit;
        private readonly EffectParameter zBias_Lighting;
        private readonly EffectParameter fog;
        private readonly EffectParameter lightVector_ZFar;
        private readonly EffectParameter headlightPosition;
        private readonly EffectParameter headlightDirection;
        private readonly EffectParameter headlightRcpDistance;
        private readonly EffectParameter headlightColor;
        private readonly EffectParameter overcast;
        private readonly EffectParameter viewerPos;
        private readonly EffectParameter imageTextureIsNight;
        private readonly EffectParameter nightColorModifier;
        private readonly EffectParameter halfNightColorModifier;
        private readonly EffectParameter vegetationAmbientModifier;
        private readonly EffectParameter signalLightIntensity;
        private readonly EffectParameter eyeVector;
        private readonly EffectParameter sideVector;
        private readonly EffectParameter imageTexture;
        private readonly EffectParameter overlayTexture;
        private readonly EffectParameter referenceAlpha;
        private readonly EffectParameter overlayScale;

        private Vector3 viewEyeVector;
        private Vector4 _zBias_Lighting;
        private Vector3 _sunDirection;
        private bool _imageTextureIsNight;

        private readonly float fullBrightness;

        //const float HalfShadowBrightness = 0.75;
        private const float HalfNightBrightness = 0.6f;
        private const float ShadowBrightness = 0.5f;
        private const float NightBrightness = 0.2f;

        // The following constants define the beginning and the end conditions of
        // the day-night transition. Values refer to the Y postion of LightVector.
        private const float startNightTrans = 0.1f;
        private const float finishNightTrans = -0.1f;

        public void SetViewMatrix(ref Matrix v)
        {
            viewEyeVector = new Vector3(v.M13, v.M23, v.M33);
            Vector3.Normalize(ref viewEyeVector, out viewEyeVector);

            Vector3.Dot(ref viewEyeVector, ref _sunDirection, out float dot);
            eyeVector.SetValue(new Vector4(viewEyeVector, dot * 0.5f + 0.5f));
            sideVector.SetValue(Vector3.Normalize(Vector3.Cross(viewEyeVector, Vector3.Down)));

            //            _eyeVector = Vector3.Normalize((new Vector3(v.M13, v.M23, v.M33));
            //eyeVector.SetValue(new Vector4(viewEyeVector, Vector3.Dot(viewEyeVector, _sunDirection) * 0.5f + 0.5f));
            //sideVector.SetValue(Vector3.Normalize(Vector3.Cross(viewEyeVector, Vector3.Down)));
        }

        public void SetMatrix(in Matrix w, in Matrix vp)
        {
            world.SetValue(w);
            MatrixExtension.Multiply(in w, in vp, out Matrix wvp);
            worldViewProjection.SetValue(wvp);
//            worldViewProjection.SetValue(w * vp);

            if (_imageTextureIsNight)
            {
                nightColorModifier.SetValue(fullBrightness);
                halfNightColorModifier.SetValue(fullBrightness);
                vegetationAmbientModifier.SetValue(fullBrightness);
            }
            else
            {
                var nightEffect = MathHelper.Clamp((_sunDirection.Y - finishNightTrans) / (startNightTrans - finishNightTrans), 0, 1);

                nightColorModifier.SetValue(MathHelper.Lerp(NightBrightness, fullBrightness, nightEffect));
                halfNightColorModifier.SetValue(MathHelper.Lerp(HalfNightBrightness, fullBrightness, nightEffect));
                vegetationAmbientModifier.SetValue(MathHelper.Lerp(ShadowBrightness, fullBrightness, _zBias_Lighting.Y));
            }
        }

        public void SetShadowMap(Matrix[] shadowProjections, Texture2D[] textures, float[] limits)
        {
            for (var i = 0; i < RenderProcess.ShadowMapCount; i++)
            {
                lightViewProjectionShadowProjection[i].SetValue(shadowProjections[i]);
                shadowMapTextures[i].SetValue(textures[i]);
            }
            shadowMapLimit.SetValue(new Vector4(limits[0], limits.Length > 1 ? limits[1] : 0, limits.Length > 2 ? limits[2] : 0, limits.Length > 3 ? limits[3] : 0));
        }

        public void ClearShadowMap()
        {
            shadowMapLimit.SetValue(Vector4.Zero);
        }

        public float ZBias { get { return _zBias_Lighting.X; } set { _zBias_Lighting.X = value; zBias_Lighting.SetValue(_zBias_Lighting); } }
        public float LightingDiffuse { get { return _zBias_Lighting.Y; } set { _zBias_Lighting.Y = value; zBias_Lighting.SetValue(_zBias_Lighting); } }
        public float LightingSpecular
        {
            get { return _zBias_Lighting.Z; }
            set
            {
                // Setting this exponent of HLSL pow() function to 0 in DX11 leads to undefined result. (HLSL bug?)
                _zBias_Lighting.Z = value >= 1 ? value : 1;
                _zBias_Lighting.W = value >= 1 ? 1 : 0;
                zBias_Lighting.SetValue(_zBias_Lighting);
            }
        }

        public void SetFog(float depth, ref Color color)
        {
            fog.SetValue(new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, 1f / depth));
        }

        public void SetLightVector_ZFar(Vector3 sunDirection, int zFar)
        {
            _sunDirection = sunDirection;
            lightVector_ZFar.SetValue(new Vector4(sunDirection.X, sunDirection.Y, sunDirection.Z, zFar));
        }

        public void SetHeadlight(ref Vector3 position, ref Vector3 direction, float distance, float minDotProduct, float fadeTime, float fadeDuration, float clampValue, ref Vector4 color)
        {
            var lighting = fadeTime / fadeDuration * clampValue;
            if (lighting < 0) lighting = 1 + lighting;
            headlightPosition.SetValue(new Vector4(position, MathHelper.Clamp(lighting, 0, clampValue)));
            headlightDirection.SetValue(new Vector4(direction, 0.5f * (1 - minDotProduct))); // We want 50% brightness at the given dot product.
            headlightRcpDistance.SetValue(1f / distance); // Needed to be separated (direction * distance) because no pre-shaders are supported in XNA 4
            headlightColor.SetValue(color);
        }

        public void SetHeadlightOff()
        {
            headlightPosition.SetValue(Vector4.Zero);
        }

        public float SignalLightIntensity { set { signalLightIntensity.SetValue(value); } }

        public float Overcast { set { overcast.SetValue(new Vector2(value, value / 2)); } }

        public Vector3 ViewerPos { set { viewerPos.SetValue(value); } }

        public bool ImageTextureIsNight { set { _imageTextureIsNight = value; imageTextureIsNight.SetValue(value ? 1f : 0f); } }

        public Texture2D ImageTexture { set { imageTexture.SetValue(value); } }

        public Texture2D OverlayTexture { set { overlayTexture.SetValue(value); } }

        public int ReferenceAlpha { set { referenceAlpha.SetValue(value / 255f); } }

        public float OverlayScale { set { overlayScale.SetValue(value); } }

        public SceneryShader(GraphicsDevice graphicsDevice)
            : base(graphicsDevice, "SceneryShader")
        {
            world = Parameters["World"];
            worldViewProjection = Parameters["WorldViewProjection"];
            lightViewProjectionShadowProjection = new EffectParameter[RenderProcess.ShadowMapCountMaximum];
            shadowMapTextures = new EffectParameter[RenderProcess.ShadowMapCountMaximum];
            for (var i = 0; i < RenderProcess.ShadowMapCountMaximum; i++)
            {
                lightViewProjectionShadowProjection[i] = Parameters["LightViewProjectionShadowProjection" + i];
                shadowMapTextures[i] = Parameters["ShadowMapTexture" + i];
            }
            shadowMapLimit = Parameters["ShadowMapLimit"];
            zBias_Lighting = Parameters["ZBias_Lighting"];
            fog = Parameters["Fog"];
            lightVector_ZFar = Parameters["LightVector_ZFar"];
            headlightPosition = Parameters["HeadlightPosition"];
            headlightDirection = Parameters["HeadlightDirection"];
            headlightRcpDistance = Parameters["HeadlightRcpDistance"];
            headlightColor = Parameters["HeadlightColor"];
            overcast = Parameters["Overcast"];
            viewerPos = Parameters["ViewerPos"];
            imageTextureIsNight = Parameters["ImageTextureIsNight"];
            nightColorModifier = Parameters["NightColorModifier"];
            halfNightColorModifier = Parameters["HalfNightColorModifier"];
            vegetationAmbientModifier = Parameters["VegetationAmbientModifier"];
            signalLightIntensity = Parameters["SignalLightIntensity"];
            eyeVector = Parameters["EyeVector"];
            sideVector = Parameters["SideVector"];
            imageTexture = Parameters["ImageTexture"];
            overlayTexture = Parameters["OverlayTexture"];
            referenceAlpha = Parameters["ReferenceAlpha"];
            overlayScale = Parameters["OverlayScale"];

            fullBrightness = Simulator.Instance.Settings.DayAmbientLight / 20.0f;

        }
    }

    public class ShadowMapShader : BaseShader
    {
        private readonly EffectParameter worldViewProjection;
        private readonly EffectParameter sideVector;
        private readonly EffectParameter imageBlurStep;
        private readonly EffectParameter imageTexture;
        private readonly EffectParameter blurTexture;

        public void SetData(ref Matrix v)
        {
            var eyeVector = Vector3.Normalize(new Vector3(v.M13, v.M23, v.M33));
            sideVector.SetValue(Vector3.Normalize(Vector3.Cross(eyeVector, Vector3.Down)));
        }

        public void SetData(ref Matrix wvp, Texture2D texture)
        {
            worldViewProjection.SetValue(wvp);
            imageTexture.SetValue(texture);
        }

        public void SetBlurData(ref Matrix wvp)
        {
            worldViewProjection.SetValue(wvp);
        }

        public void SetBlurData(Texture2D texture)
        {
            blurTexture.SetValue(texture);
            imageBlurStep.SetValue(texture != null ? 1f / texture.Width : 0);
        }

        public ShadowMapShader(GraphicsDevice graphicsDevice)
            : base(graphicsDevice, "ShadowMap")
        {
            worldViewProjection = Parameters["WorldViewProjection"];
            sideVector = Parameters["SideVector"];
            imageBlurStep = Parameters["ImageBlurStep"];
            imageTexture = Parameters["ImageTexture"];
            blurTexture = Parameters["BlurTexture"];
        }
    }

    public class SkyShader : BaseShader
    {
        private readonly EffectParameter worldViewProjection;
        private readonly EffectParameter lightVector;
        private readonly EffectParameter time;
        private readonly EffectParameter overcast;
        private readonly EffectParameter windDisplacement;
        private readonly EffectParameter skyColor;
        private readonly EffectParameter fogColor;
        private readonly EffectParameter fog;
        private readonly EffectParameter moonColor;
        private readonly EffectParameter moonTexCoord;
        private readonly EffectParameter cloudColor;
        private readonly EffectParameter rightVector;
        private readonly EffectParameter upVector;
        private readonly EffectParameter skyMapTexture;
        private readonly EffectParameter starMapTexture;
        private readonly EffectParameter moonMapTexture;
        private readonly EffectParameter moonMaskTexture;
        private readonly EffectParameter cloudMapTexture;


        public Vector3 LightVector
        {
            set
            {
                lightVector.SetValue(new Vector4(value, 1f / value.Length()));

                cloudColor.SetValue(Day2Night(0.2f, -0.2f, 0.15f, value.Y));
                var skyColor1 = Day2Night(0.25f, -0.25f, -0.5f, value.Y);
                var skyColor2 = MathHelper.Clamp(skyColor1 + 0.55f, 0, 1);
                var skyColor3 = 0.001f / (0.8f * Math.Abs(value.Y - 0.1f));
                skyColor.SetValue(new Vector3(skyColor1, skyColor2, skyColor3)); 

                // Fade moon during daylight
                var moonColor1 = value.Y > 0.1f ? (1 - value.Y) / 1.5f : 1;
                // Mask stars behind dark side (mask fades in)
                var moonColor2 = _moonPhase != 6 && value.Y < 0.13 ? -6.25f * value.Y + 0.8125f : 0;
                moonColor.SetValue(new Vector2(moonColor1, moonColor2));
            }
        }

        public void SetFog(float depth, ref Color color)
        {
            fogColor.SetValue(new Vector3(color.R / 255f, color.G / 255f, color.B / 255f));
            fog.SetValue(new Vector4(5000f / depth, 0.015f * MathHelper.Clamp(depth / 5000f, 0, 1), MathHelper.Clamp(depth / 10000f, 0, 1), 0.05f * MathHelper.Clamp(depth / 10000f, 0, 1)));
        }

        private float _time;
        public float Time
        {
            set
            {
                _time = value;
                time.SetValue(value);
            }
        }

        private int _moonPhase;
        public float Random
        {
            set 
            { 
                _moonPhase = (int)value; 
                moonTexCoord.SetValue(new Vector2((value % 2) / 2, (int)(value / 2) / 4));
            }
        }

        public float Overcast
        {
            set
            {
                if (value < 0.2f)
                    overcast.SetValue(new Vector4(4 * value + 0.2f, 0.0f, 0.0f, 0.0f));
                else
                    // Coefficients selected by author to achieve the desired appearance
                    overcast.SetValue(new Vector4(MathHelper.Clamp(2 * value - 0.4f, 0, 1), 1.25f - 1.125f * value, 1.15f - 0.75f * value, 1f));
            }
        }

        public float WindSpeed { get; set; }

        public float WindDirection
        {
            set 
            {
                var totalWindDisplacement = 50 * WindSpeed * _time; // This exaggerates the wind speed, but it is necessary to get a visible effect
                windDisplacement.SetValue(new Vector2(-(float)Math.Sin(value) * totalWindDisplacement, (float)Math.Cos(value) * totalWindDisplacement));
            }
        }

        public float MoonScale { get; set; }

        public Texture2D SkyMapTexture { set { skyMapTexture.SetValue(value); } }
        public Texture2D StarMapTexture { set { starMapTexture.SetValue(value); } }
        public Texture2D MoonMapTexture { set { moonMapTexture.SetValue(value); } }
        public Texture2D MoonMaskTexture { set { moonMaskTexture.SetValue(value); } }
        public Texture2D CloudMapTexture { set { cloudMapTexture.SetValue(value); } }

        public void SetViewMatrix(ref Matrix view)
        {
            var moonScale = MoonScale;
            if (_moonPhase == 6)
                moonScale *= 2;

            var eye = Vector3.Normalize(new Vector3(view.M13, view.M23, view.M33));
            var right = Vector3.Cross(eye, Vector3.Up);
            Vector3.Cross(ref right, ref eye, out Vector3 up);

            rightVector.SetValue(right * moonScale);
            upVector.SetValue(up * moonScale);
        }

        public void SetMatrix(ref Matrix wvp)
        {
            worldViewProjection.SetValueTranspose(wvp);
        }

        public SkyShader(GraphicsDevice graphicsDevice)
            : base(graphicsDevice, "SkyShader")
        {
            worldViewProjection = Parameters["WorldViewProjection"];
            lightVector = Parameters["LightVector"];
            time = Parameters["Time"];
            overcast = Parameters["Overcast"];
            windDisplacement = Parameters["WindDisplacement"];
            skyColor = Parameters["SkyColor"];
            fogColor = Parameters["FogColor"];
            fog = Parameters["Fog"];
            moonColor = Parameters["MoonColor"];
            moonTexCoord = Parameters["MoonTexCoord"];
            cloudColor = Parameters["CloudColor"];
            rightVector = Parameters["RightVector"];
            upVector = Parameters["UpVector"];
            skyMapTexture = Parameters["SkyMapTexture"];
            starMapTexture = Parameters["StarMapTexture"];
            moonMapTexture = Parameters["MoonMapTexture"];
            moonMaskTexture = Parameters["MoonMaskTexture"];
            cloudMapTexture = Parameters["CloudMapTexture"];
        }


        // This function dims the lighting at night, with a transition period as the sun rises or sets
        private static float Day2Night(float startNightTrans, float finishNightTrans, float minDarknessCoeff, float sunDirectionY)
        {
            int vIn = Simulator.Instance.Settings.DayAmbientLight;
            float dayAmbientLight = (float)vIn / 20.0f ;
              
            // The following two are used to interpoate between day and night lighting (y = mx + b)
            var slope = (dayAmbientLight - minDarknessCoeff) / (startNightTrans - finishNightTrans); // "m"
            var incpt = dayAmbientLight - slope * startNightTrans; // "b"
            // This is the return value used to darken scenery
            float adjustment;

            if (sunDirectionY < finishNightTrans)
                adjustment = minDarknessCoeff;
            else if (sunDirectionY > startNightTrans)
                adjustment = dayAmbientLight; // Scenery is fully lit during the day
            else
                adjustment = slope * sunDirectionY + incpt;

            return adjustment;
        }
    }

    public class ParticleEmitterShader : BaseShader
    {
        private EffectParameter emitSize;
        private EffectParameter tileXY;
        private EffectParameter currentTime;
        private EffectParameter wvp;
        private EffectParameter invView;
        private EffectParameter texture;
        private EffectParameter lightVector;
        private EffectParameter fog;

        public float CurrentTime
        {
            set { currentTime.SetValue(value); }
        }

        public Vector2 CameraTileXY
        {
            set { tileXY.SetValue(value); }
        }

        public Texture2D Texture
        {
            set { texture.SetValue(value); }
        }

        public float EmitSize
        {
            set { emitSize.SetValue(value); }
        }

        public Vector3 LightVector
        {
            set { lightVector.SetValue(value); }
        }

        public ParticleEmitterShader(GraphicsDevice graphicsDevice)
            : base(graphicsDevice, "ParticleEmitterShader")
        {
            emitSize = Parameters["emitSize"];
            currentTime = Parameters["currentTime"];
            wvp = Parameters["worldViewProjection"];
            invView = Parameters["invView"];
            tileXY = Parameters["cameraTileXY"];
            texture = Parameters["particle_Tex"];
            lightVector = Parameters["LightVector"];
            fog = Parameters["Fog"];
        }

        public void SetMatrix(ref Matrix view, ref Matrix projection)
        {
            wvp.SetValue(Matrix.Identity * view * projection);
            invView.SetValue(Matrix.Invert(view));
        }

        public void SetFog(float depth, ref Color color)
        {
            fog.SetValue(new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, MathHelper.Clamp(300f / depth, 0, 1)));
        }
    }

    public class LightGlowShader : BaseShader
    {
        private readonly EffectParameter worldViewProjection;
        private readonly EffectParameter fade;
        private readonly EffectParameter lightGlowTexture;

        public Texture2D LightGlowTexture { set { lightGlowTexture.SetValue(value); } }

        public void SetMatrix(ref Matrix wvp)
        {
            worldViewProjection.SetValueTranspose(wvp);
        }

        public void SetFade(Vector2 fadeValues)
        {
            fade.SetValue(fadeValues);
        }

        public LightGlowShader(GraphicsDevice graphicsDevice)
            : base(graphicsDevice, "LightGlowShader")
        {
            worldViewProjection = Parameters["WorldViewProjection"];
            fade = Parameters["Fade"];
            lightGlowTexture = Parameters["LightGlowTexture"];
        }
    }

    public class LightConeShader : BaseShader
    {
        private EffectParameter worldViewProjection;
        private EffectParameter fade;

        public LightConeShader(GraphicsDevice graphicsDevice)
            : base(graphicsDevice, "LightConeShader")
        {
            worldViewProjection = Parameters["WorldViewProjection"];
            fade = Parameters["Fade"];
        }

        public void SetMatrix(ref Matrix wvp)
        {
            worldViewProjection.SetValue(wvp);
        }

        public void SetFade(Vector2 fadeValues)
        {
            fade.SetValue(fadeValues);
        }
    }

    public class PopupWindowShader : BaseShader
    {
        private readonly EffectParameter world;
        private readonly EffectParameter worldViewProjection;
        private readonly EffectParameter glassColor;
        private readonly EffectParameter screenSize;
        private readonly EffectParameter screenTexture;

        public Texture2D Screen
        {
            set
            {
                screenTexture.SetValue(value);
                if (value == null)
                    screenSize.SetValue(new Vector2(0, 0));
                else
                    screenSize.SetValue(new Vector2(value.Width, value.Height));
            }
        }

        public Color GlassColor { set { glassColor.SetValue(new Vector3(value.R / 255f, value.G / 255f, value.B / 255f)); } }

        public void SetMatrix(in Matrix w, ref Matrix wvp)
        {
            world.SetValue(w);
            worldViewProjection.SetValue(wvp);
        }

        public PopupWindowShader(Viewer viewer, GraphicsDevice graphicsDevice)
            : base(graphicsDevice, "PopupWindow")
        {
            world = Parameters["World"];
            worldViewProjection = Parameters["WorldViewProjection"];
            glassColor = Parameters["GlassColor"];
            screenSize = Parameters["ScreenSize"];
            screenTexture = Parameters["ScreenTexture"];
            // TODO: This should happen on the loader thread.
            Parameters["WindowTexture"].SetValue(SharedTextureManager.Get(graphicsDevice, System.IO.Path.Combine(viewer.ContentPath, "Window.png")));
        }
    }

    public class CabShader : BaseShader
    {
        private readonly EffectParameter nightColorModifier;
        private readonly EffectParameter lightOn;
        private readonly EffectParameter light1Pos;
        private readonly EffectParameter light2Pos;
        private readonly EffectParameter light1Col;
        private readonly EffectParameter light2Col;
        private readonly EffectParameter texPos;
        private readonly EffectParameter texSize;
        private readonly EffectParameter imageTexture;

        public void SetTextureData(float x, float y, float width, float height)
        {
            texPos.SetValue(new Vector2(x, y));
            texSize.SetValue(new Vector2(width, height));
        }

        public void SetLightPositions (Vector4 light1Position, Vector4 light2Position)
        {
            light1Pos.SetValue(light1Position);
            light2Pos.SetValue(light2Position);
        }

        public void SetData(Vector3 sunDirection, bool isNightTexture, bool isDashLight, float overcast)
        {
            nightColorModifier.SetValue(MathHelper.Lerp(0.2f + (isDashLight ? 0.15f : 0), 1, isNightTexture ? 1 : MathHelper.Clamp((sunDirection.Y + 0.1f) / 0.2f, 0, 1) * MathHelper.Clamp(1.5f - overcast, 0, 1)));
            lightOn.SetValue(isDashLight);
        }

        public CabShader(GraphicsDevice graphicsDevice, Vector4 light1Position, Vector4 light2Position, Vector3 light1Color, Vector3 light2Color)
            : base(graphicsDevice, "CabShader")
        {
            nightColorModifier = Parameters["NightColorModifier"];
            lightOn = Parameters["LightOn"];
            light1Pos = Parameters["Light1Pos"];
            light2Pos = Parameters["Light2Pos"];
            light1Col = Parameters["Light1Col"];
            light2Col = Parameters["Light2Col"];
            texPos = Parameters["TexPos"];
            texSize = Parameters["TexSize"];
            imageTexture = Parameters["ImageTexture"];

            light1Pos.SetValue(light1Position);
            light2Pos.SetValue(light2Position);
            light1Col.SetValue(light1Color);
            light2Col.SetValue(light2Color);
        }
    }

    public class DriverMachineInterfaceShader : BaseShader
    {
        private readonly EffectParameter limitAngle;
        private readonly EffectParameter normalColor;
        private readonly EffectParameter limitColor;
        private readonly EffectParameter pointerColor;
        private readonly EffectParameter interventionColor;
        private readonly EffectParameter backgroundColor;
        private readonly EffectParameter imageTexture;

        public void SetData(Vector4 angle, Color gaugeColor, Color needleColor, Color overspeedColor)
        {
            limitAngle.SetValue(angle);
            limitColor.SetValue(gaugeColor.ToVector4());
            pointerColor.SetValue(needleColor.ToVector4());
            interventionColor.SetValue(overspeedColor.ToVector4());
        }

        public DriverMachineInterfaceShader(GraphicsDevice graphicsDevice)
            : base(graphicsDevice, "DriverMachineInterfaceShader")
        {
            normalColor = Parameters["NormalColor"];
            limitColor = Parameters["LimitColor"];
            pointerColor = Parameters["PointerColor"];
            backgroundColor = Parameters["BackgroundColor"];
            limitAngle = Parameters["LimitAngle"];
            imageTexture = Parameters["ImageTexture"];
            interventionColor = Parameters["InterventionColor"];
        }
    }

    public class DebugShader : BaseShader
    {
        private readonly EffectParameter worldViewProjection;
        private readonly EffectParameter screenSize;
        private readonly EffectParameter graphPos;
        private readonly EffectParameter graphSample;

        public Vector2 ScreenSize { set { screenSize.SetValue(value); } }

        public Vector4 GraphPos { set { graphPos.SetValue(value); } }

        public Vector2 GraphSample { set { graphSample.SetValue(value); } }

        public DebugShader(GraphicsDevice graphicsDevice)
            : base(graphicsDevice, "DebugShader")
        {
            worldViewProjection = Parameters["WorldViewProjection"];
            screenSize = Parameters["ScreenSize"];
            graphPos = Parameters["GraphPos"];
            graphSample = Parameters["GraphSample"];
        }

        public void SetMatrix(Matrix matrix, ref Matrix viewproj)
        {
            MatrixExtension.Multiply(in matrix, in viewproj, out Matrix wvp);
            worldViewProjection.SetValue(wvp);
        }
    }
}
