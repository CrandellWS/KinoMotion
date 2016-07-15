//
// Kino/Motion - Motion blur effect
//
// Copyright (C) 2016 Keijiro Takahashi
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
using UnityEngine;

namespace Kino
{
    public partial class Motion
    {
        // Reconstruction filter for shutter speed simulation
        class ReconstructionFilter
        {
            #region Predefined constants

            // The maximum length of motion blur, given as a percentage
            // of the screen height. Larger values may introduce artifacts.
            const float kMaxBlurRadius = 5;

            // Texture format for storing packed velocity/depth.
            const RenderTextureFormat kPackedRTFormat = RenderTextureFormat.ARGB2101010;

            // Texture format for storing 2D vectors.
            const RenderTextureFormat kVectorRTFormat = RenderTextureFormat.RGHalf;

            #endregion

            #region Public methods

            public ReconstructionFilter()
            {
                _material = new Material(Shader.Find("Hidden/Kino/Motion/Reconstruction"));
                _material.hideFlags = HideFlags.DontSave;
            }

            public void Release()
            {
                DestroyImmediate(_material);
                _material = null;
            }

            public void ProcessImage(
                float shutterAngle, int sampleCount,
                RenderTexture source, RenderTexture destination
            )
            {
                // Calculate the maximum blur radius in pixels.
                var maxBlurPixels = (int)(kMaxBlurRadius * source.height / 100);

                // Calculate the TileMax size.
                // It should be a multiple of 8 and larger than maxBlur.
                var tileSize = ((maxBlurPixels - 1) / 8 + 1) * 8;

                // 1st pass - Velocity/depth packing
                // Motion vectors are scaled by an empirical factor of 1.45.
                var velocityScale = shutterAngle / 360 * 1.45f;
                _material.SetFloat("_VelocityScale", velocityScale);
                _material.SetFloat("_MaxBlurRadius", maxBlurPixels);

                var vbuffer = GetTemporaryRT(source, 1, kPackedRTFormat);
                Graphics.Blit(null, vbuffer, _material, 0);

                // 2nd pass - 1/4 TileMax filter
                var tile4 = GetTemporaryRT(source, 4, kVectorRTFormat);
                Graphics.Blit(vbuffer, tile4, _material, 1);

                // 3rd pass - 1/2 TileMax filter
                var tile8 = GetTemporaryRT(source, 8, kVectorRTFormat);
                Graphics.Blit(tile4, tile8, _material, 2);
                ReleaseTemporaryRT(tile4);

                // 4th pass - Last TileMax filter (reduce to tileSize)
                var tileMaxOffs = Vector2.one * (tileSize / 8.0f - 1) * -0.5f;
                _material.SetVector("_TileMaxOffs", tileMaxOffs);
                _material.SetInt("_TileMaxLoop", tileSize / 8);

                var tile = GetTemporaryRT(source, tileSize, kVectorRTFormat);
                Graphics.Blit(tile8, tile, _material, 3);
                ReleaseTemporaryRT(tile8);

                // 5th pass - NeighborMax filter
                var neighborMax = GetTemporaryRT(source, tileSize, kVectorRTFormat);
                Graphics.Blit(tile, neighborMax, _material, 4);
                ReleaseTemporaryRT(tile);

                // 6th pass - Reconstruction pass
                _material.SetInt("_LoopCount", Mathf.Clamp(sampleCount / 2, 1, 64));
                _material.SetFloat("_MaxBlurRadius", maxBlurPixels);
                _material.SetTexture("_NeighborMaxTex", neighborMax);
                _material.SetTexture("_VelocityTex", vbuffer);
                Graphics.Blit(source, destination, _material, 5);

                // Cleaning up
                ReleaseTemporaryRT(vbuffer);
                ReleaseTemporaryRT(neighborMax);
            }

            #endregion

            #region Private members

            Material _material;

            RenderTexture GetTemporaryRT(
                Texture source, int divider, RenderTextureFormat format
            )
            {
                var w = source.width / divider;
                var h = source.height / divider;
                var rt = RenderTexture.GetTemporary(w, h, 0, format);
                rt.filterMode = FilterMode.Point;
                return rt;
            }

            void ReleaseTemporaryRT(RenderTexture rt)
            {
                RenderTexture.ReleaseTemporary(rt);
            }

            #endregion
        }
    }
}
