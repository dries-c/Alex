﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Alex.API.Entities;
using Alex.API.Graphics;
using Alex.API.Utils;
using Alex.ResourcePackLib.Json.Models.Entities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Alex.Graphics.Models.Entity
{
	public partial class EntityModelRenderer
	{
		public class ModelBone : IDisposable, IAttachable
		{
			private Texture2D Texture { get; set; }
			private PooledIndexBuffer Buffer { get; set; }
			private List<IAttachable> Children { get; set; } = new List<IAttachable>();
			
			private Vector3 _rotation = Vector3.Zero;

			public Vector3 Rotation
			{
				get { return _rotation; }
				set { _rotation = value; }
			}

			public bool Rendered { get; set; } = true;

			public ModelBone Parent { get; set; } = null;
			
			public Queue<ModelBoneAnimation> Animations { get; }
			private ModelBoneAnimation CurrentAnim { get; set; } = null;
			public bool IsAnimating => CurrentAnim != null || Animations.Count > 0;
			internal EntityModelBone Definition { get; }
			
			private short[] Indices { get; }
			public ModelBone(Texture2D texture, short[] indices, EntityModelBone bone, Matrix defaultMatrix)
			{
				Texture = texture;
				Definition = bone;
				Indices = indices;
				Animations = new Queue<ModelBoneAnimation>();

				DefaultMatrix = defaultMatrix;
			}

			//private bool _isDirty = true;

			private bool _applyHeadYaw = false;
			private bool _applyPitch = false;

			public bool ApplyHeadYaw
			{
				get
				{
					return _applyHeadYaw;
				}
				set
				{
					_applyHeadYaw = value;
					
					foreach (var child in Children)
					{
						child.ApplyHeadYaw = value;
					}
				}
			}

			public bool ApplyPitch 
			{
				get
				{
					return _applyPitch;
				}
				set
				{
					_applyPitch = value;

					foreach (var child in Children)
					{
						child.ApplyPitch = value;
					}
				}
			}
			
			private AlphaTestEffect Effect { get; set; }
			
			private object _disposeLock = new object();
			public void Render(IRenderArgs args, bool mock, out int vertices)
			{
				vertices = 0;
				
				if (_disposed) return;
				if (!Monitor.TryEnter(_disposeLock, 0))
					return;

				try
				{
					if (!Definition.NeverRender && Rendered)
					{
						var buffer = Buffer;
						var effect = Effect;

						if (!(buffer == null || effect == null || effect.Texture == null || effect.IsDisposed
						      || buffer.MarkedForDisposal))
						{
							if (buffer.IndexCount == 0)
							{
								Log.Warn($"Bone indexcount = 0 || {Definition.Name}");
							}

							args.GraphicsDevice.Indices = buffer;

							effect.View = args.Camera.ViewMatrix;
							effect.Projection = args.Camera.ProjectionMatrix;

							if (!mock && buffer.IndexCount > 0)
							{
								if (effect.CurrentTechnique != null && !Definition.NeverRender)
								{
									//	foreach (var technique in effect.Techniques)
									{
										foreach (var pass in effect.CurrentTechnique.Passes)
										{
											pass?.Apply();

											args.GraphicsDevice.DrawIndexedPrimitives(
												PrimitiveType.TriangleList, 0, 0, buffer.IndexCount / 3);
										}
									}
								}

								//else
								{
									//	Log.Warn($"Current");
								}

								vertices += buffer.IndexCount / 3;
							}
							else if (!mock && buffer.IndexCount == 0)
							{
								Log.Warn($"Index count = 0");
							}
						}
					}

					var children = Children.ToArray();

					if (children.Length > 0)
					{
						foreach (var child in children)
						{
							child.Render(args, mock, out int childVertices);
							vertices += childVertices;
						}
					}
				}
				catch (Exception ex)
				{
					Log.Warn(ex, $"An error occured rendering bone {Name}");
				}
				finally
				{
					Monitor.Exit(_disposeLock);
				}
			}

			public void ClearAnimations()
			{
				if (_disposed) return;
				var anim = CurrentAnim;

				if (anim != null)
				{
					anim.Reset();
					CurrentAnim = null;
				}
			}

			private Matrix DefaultMatrix { get; set; } = Matrix.Identity;

			public void Update(IUpdateArgs args,
				Matrix characterMatrix,
				Vector3 diffuseColor,
				PlayerLocation modelLocation)
			{
				if (_disposed || Effect == null) return;

				//if (!Monitor.TryEnter(_disposeLock, 0))
				//	return;

				try
				{
					//var device = args.GraphicsDevice;

					if (CurrentAnim == null && Animations.TryDequeue(out var animation))
					{
						animation.Setup();
						animation.Start();

						CurrentAnim = animation;
					}

					if (CurrentAnim != null)
					{
						CurrentAnim.Update(args.GameTime);

						if (CurrentAnim.IsFinished())
						{
							CurrentAnim.Reset();
							CurrentAnim = null;
						}
					}
					
					Matrix yawPitchMatrix = Matrix.Identity;

					var pivot = Definition.Pivot * new Vector3(-1f, 1f, 1f);
					if (ApplyHeadYaw || ApplyPitch)
					{
						var headYaw = ApplyHeadYaw ? MathUtils.ToRadians(-(modelLocation.HeadYaw - modelLocation.Yaw)) :
							0f;

						var pitch = ApplyPitch ? MathUtils.ToRadians(modelLocation.Pitch) : 0f;

						yawPitchMatrix = Matrix.CreateTranslation(-pivot)
						                 * Matrix.CreateFromYawPitchRoll(headYaw, pitch, 0f)
						                 * Matrix.CreateTranslation(pivot);
					}

					var userRotationMatrix = Matrix.CreateTranslation(-pivot)
					                         * Matrix.CreateRotationX(MathUtils.ToRadians(Rotation.X))
					                         * Matrix.CreateRotationY(MathUtils.ToRadians(Rotation.Y))
					                         * Matrix.CreateRotationZ(MathUtils.ToRadians(Rotation.Z))
					                         * Matrix.CreateTranslation(pivot);

					var world    = yawPitchMatrix * userRotationMatrix * DefaultMatrix * characterMatrix;
					var forward  = world.Forward;
					var backward = world.Backward;
					var left     = world.Left;
					var right    = world.Right;

					world.Left = right;
					world.Right = left;
				//	world.Forward = backward;
					//world.Backward = forward;

					Effect.World = world;
					Effect.DiffuseColor = diffuseColor;
					var children = Children.ToArray();

					if (children.Length > 0)
					{
						foreach (var child in children)
						{
							child.Update(args, userRotationMatrix * characterMatrix, diffuseColor, modelLocation);
						}
					}

					//if (_isDirty || Indices.Length > Buffer.IndexCount)
					{
						UpdateVertexBuffer(args.GraphicsDevice);
						//	_isDirty = false;
					}
				}
				finally
				{
				//	Monitor.Exit(_disposeLock);
				}
			}

			private bool _updateQueued = false;
			private void UpdateVertexBuffer(GraphicsDevice device)
			{
				if (_disposed || _updateQueued) return;
				
				var               indices       = Indices;
				PooledIndexBuffer currentBuffer = Buffer;
				if (indices.Length > 0 && (Buffer == null || currentBuffer.IndexCount != indices.Length))
				{
					_updateQueued = true;
					
					Alex.Instance.UIThreadQueue.Enqueue(
						() =>
						{
							if (_disposed)
								return;
							
							PooledIndexBuffer buffer = GpuResourceManager.GetIndexBuffer(
								this, device, IndexElementSize.SixteenBits, indices.Length, BufferUsage.None);

							buffer.SetData(indices);
							Buffer = buffer;

							currentBuffer?.MarkForDisposal();
							
							_updateQueued = false;
						});
				}
			}

			internal void SetTexture(PooledTexture2D texture)
			{
				if (_disposed) return;
				
				if (Effect != null)
				{
					Effect.Texture = texture;
				}

				Texture = texture;
			}

			private bool _disposed = false;
			public void Dispose()
			{
				_disposed = true;
				lock (_disposeLock)
				{
					Effect?.Dispose();
					Buffer?.MarkForDisposal();
				}
			}

			public void AddChild(IAttachable modelBone)
			{
				if (!Children.Contains(modelBone))
				{
					Children.Add(modelBone);
				}
				else
				{
					Log.Warn($"Could not add {modelBone.Name} as child of {Definition.Name}");
				}
			}

			public void Remove(IAttachable modelBone)
			{
				if (Children.Contains(modelBone))
				{
					Children.Remove(modelBone);
				}
			}

			public string Name => Definition.Name;

			public void Setup(GraphicsDevice device)
			{
				if (Effect == null)
				{
					Effect = new AlphaTestEffect(device);
					Effect.Texture = Texture;
					Effect.VertexColorEnabled = true;

				}
			}
		}
	}
}
