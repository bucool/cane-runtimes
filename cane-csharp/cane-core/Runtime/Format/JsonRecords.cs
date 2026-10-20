// Generated from specification/runtime/RUNTIME_V2_BINARY_LAYOUT.json.
namespace Cane.Format
{
    internal static partial class Records
    {
        internal static ModelRecord ReadModel(JsonInput r)
        {
            var value = new ModelRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "name": value.Name = r.String(); seen |= 1UL; break;
                    case "referenceScale": value.ReferenceScale = r.Number(); seen |= 2UL; break;
                    case "bones": value.Bones = r.List(x => ReadBone(x)); seen |= 4UL; break;
                    case "constraints": value.Constraints = r.List(x => ReadConstraint(x)); seen |= 8UL; break;
                    case "slots": value.Slots = r.List(x => ReadSlot(x)); seen |= 16UL; break;
                    case "keys": value.Keys = r.List(x => ReadKey(x)); seen |= 32UL; break;
                    case "geometries": value.Geometries = r.List(x => ReadGeometry(x)); seen |= 64UL; break;
                    case "attachments": value.Attachments = r.List(x => ReadAttachment(x)); seen |= 128UL; break;
                    case "sequences": value.Sequences = r.List(x => ReadSequence(x)); seen |= 256UL; break;
                    case "skins": value.Skins = r.List(x => ReadSkin(x)); seen |= 512UL; break;
                    case "defaultBindings": value.DefaultBindings = r.List(x => x.Tuple(2, x => x.Integer())); seen |= 1024UL; break;
                    case "animations": value.Animations = r.List(x => ReadAnimation(x)); seen |= 2048UL; break;
                    case "curves": value.Curves = r.UInt(); seen |= 4096UL; break;
                    case "textures": value.Textures = r.List(x => ReadTexture(x)); seen |= 8192UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 16383UL) != 16383UL) throw r.Error("Missing Model field");
            return value;
        }
        internal static BoneRecord ReadBone(JsonInput r)
        {
            var value = new BoneRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "id": value.Id = r.String(); seen |= 1UL; break;
                    case "name": value.Name = r.String(); seen |= 2UL; break;
                    case "parent": value.Parent = (r.Present() ? (uint?)r.UInt() : null); seen |= 4UL; break;
                    case "length": value.Length = r.Number(); seen |= 8UL; break;
                    case "setup": value.Setup = r.Tuple(8, x => x.Number()); seen |= 16UL; break;
                    case "skinRequired": value.SkinRequired = r.Boolean(); seen |= 32UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 63UL) != 63UL) throw r.Error("Missing Bone field");
            return value;
        }
        internal static SlotRecord ReadSlot(JsonInput r)
        {
            var value = new SlotRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "id": value.Id = r.String(); seen |= 1UL; break;
                    case "name": value.Name = r.String(); seen |= 2UL; break;
                    case "bone": value.Bone = r.UInt(); seen |= 4UL; break;
                    case "attachment": value.Attachment = (r.Present() ? (uint?)r.UInt() : null); seen |= 8UL; break;
                    case "color": value.Color = r.Tuple(4, x => x.Number()); seen |= 16UL; break;
                    case "dark": value.Dark = (r.Present() ? (double[]?)ReadRGB(r) : null); seen |= 32UL; break;
                    case "blend": value.Blend = r.UInt(); seen |= 64UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 127UL) != 127UL) throw r.Error("Missing Slot field");
            return value;
        }
        internal static double[] ReadRGB(JsonInput r)
        {
            return r.Tuple(3, x => x.Number());
        }
        internal static KeyRecord ReadKey(JsonInput r)
        {
            var value = new KeyRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "slot": value.Slot = r.UInt(); seen |= 1UL; break;
                    case "name": value.Name = r.String(); seen |= 2UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 3UL) != 3UL) throw r.Error("Missing Key field");
            return value;
        }
        internal static GeometryRecord ReadGeometry(JsonInput r)
        {
            var value = new GeometryRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "positions": value.Positions = r.UInt(); seen |= 1UL; break;
                    case "weights": value.Weights = (r.Present() ? (WeightsRecord?)ReadWeights(r) : null); seen |= 2UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 3UL) != 3UL) throw r.Error("Missing Geometry field");
            return value;
        }
        internal static WeightsRecord ReadWeights(JsonInput r)
        {
            var value = new WeightsRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "bones": value.Bones = r.UInt(); seen |= 1UL; break;
                    case "values": value.Values = r.UInt(); seen |= 2UL; break;
                    case "starts": value.Starts = r.UInt(); seen |= 4UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 7UL) != 7UL) throw r.Error("Missing Weights field");
            return value;
        }
        private static MeshAttachmentRecord ReadMeshAttachment(JsonInput r)
        {
            var value = new MeshAttachmentRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "type": if (r.String() != "mesh") throw r.Error("Invalid record type"); break;
                    case "id": value.Id = r.String(); seen |= 1UL; break;
                    case "name": value.Name = r.String(); seen |= 2UL; break;
                    case "geometry": value.Geometry = r.UInt(); seen |= 4UL; break;
                    case "texture": value.Texture = r.UInt(); seen |= 8UL; break;
                    case "uvs": value.Uvs = r.UInt(); seen |= 16UL; break;
                    case "indices": value.Indices = r.UInt(); seen |= 32UL; break;
                    case "color": value.Color = r.Tuple(4, x => x.Number()); seen |= 64UL; break;
                    case "sequence": value.Sequence = (r.Present() ? (uint?)r.UInt() : null); seen |= 128UL; break;
                    case "placement": value.Placement = r.Tuple(2, x => x.Number()); seen |= 256UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 511UL) != 511UL) throw r.Error("Missing MeshAttachment field");
            return value;
        }
        private static BoundingAttachmentRecord ReadBoundingAttachment(JsonInput r)
        {
            var value = new BoundingAttachmentRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "type": if (r.String() != "bounding") throw r.Error("Invalid record type"); break;
                    case "id": value.Id = r.String(); seen |= 1UL; break;
                    case "name": value.Name = r.String(); seen |= 2UL; break;
                    case "geometry": value.Geometry = r.UInt(); seen |= 4UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 7UL) != 7UL) throw r.Error("Missing BoundingAttachment field");
            return value;
        }
        private static PointAttachmentRecord ReadPointAttachment(JsonInput r)
        {
            var value = new PointAttachmentRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "type": if (r.String() != "point") throw r.Error("Invalid record type"); break;
                    case "id": value.Id = r.String(); seen |= 1UL; break;
                    case "name": value.Name = r.String(); seen |= 2UL; break;
                    case "geometry": value.Geometry = r.UInt(); seen |= 4UL; break;
                    case "rotation": value.Rotation = r.Number(); seen |= 8UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 15UL) != 15UL) throw r.Error("Missing PointAttachment field");
            return value;
        }
        private static PathAttachmentRecord ReadPathAttachment(JsonInput r)
        {
            var value = new PathAttachmentRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "type": if (r.String() != "path") throw r.Error("Invalid record type"); break;
                    case "id": value.Id = r.String(); seen |= 1UL; break;
                    case "name": value.Name = r.String(); seen |= 2UL; break;
                    case "geometry": value.Geometry = r.UInt(); seen |= 4UL; break;
                    case "closed": value.Closed = r.Boolean(); seen |= 8UL; break;
                    case "constantSpeed": value.ConstantSpeed = r.Boolean(); seen |= 16UL; break;
                    case "lengths": value.Lengths = r.List(x => x.Number()); seen |= 32UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 63UL) != 63UL) throw r.Error("Missing PathAttachment field");
            return value;
        }
        private static ClippingAttachmentRecord ReadClippingAttachment(JsonInput r)
        {
            var value = new ClippingAttachmentRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "type": if (r.String() != "clipping") throw r.Error("Invalid record type"); break;
                    case "id": value.Id = r.String(); seen |= 1UL; break;
                    case "name": value.Name = r.String(); seen |= 2UL; break;
                    case "geometry": value.Geometry = r.UInt(); seen |= 4UL; break;
                    case "endSlot": value.EndSlot = (r.Present() ? (uint?)r.UInt() : null); seen |= 8UL; break;
                    case "convex": value.Convex = r.Boolean(); seen |= 16UL; break;
                    case "inverse": value.Inverse = r.Boolean(); seen |= 32UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 63UL) != 63UL) throw r.Error("Missing ClippingAttachment field");
            return value;
        }
        internal static AttachmentRecord ReadAttachment(JsonInput r)
        {
            return r.Discriminator() switch
            {
                "mesh" => ReadMeshAttachment(r),
                "bounding" => ReadBoundingAttachment(r),
                "point" => ReadPointAttachment(r),
                "path" => ReadPathAttachment(r),
                "clipping" => ReadClippingAttachment(r),
                _ => throw r.Error("Unknown Attachment type")
            };
        }
        internal static SequenceRecord ReadSequence(JsonInput r)
        {
            var value = new SequenceRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "setupIndex": value.SetupIndex = r.UInt(); seen |= 1UL; break;
                    case "frames": value.Frames = r.List(x => ReadSequenceFrame(x)); seen |= 2UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 3UL) != 3UL) throw r.Error("Missing Sequence field");
            return value;
        }
        internal static SequenceFrameRecord ReadSequenceFrame(JsonInput r)
        {
            var value = new SequenceFrameRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "texture": value.Texture = r.UInt(); seen |= 1UL; break;
                    case "uvs": value.Uvs = r.UInt(); seen |= 2UL; break;
                    case "geometry": value.Geometry = (r.Present() ? (uint?)r.UInt() : null); seen |= 4UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 7UL) != 7UL) throw r.Error("Missing SequenceFrame field");
            return value;
        }
        internal static SkinRecord ReadSkin(JsonInput r)
        {
            var value = new SkinRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "id": value.Id = r.String(); seen |= 1UL; break;
                    case "name": value.Name = r.String(); seen |= 2UL; break;
                    case "bindings": value.Bindings = r.List(x => x.Tuple(2, x => x.Integer())); seen |= 4UL; break;
                    case "bones": value.Bones = r.List(x => x.UInt()); seen |= 8UL; break;
                    case "constraints": value.Constraints = r.List(x => x.UInt()); seen |= 16UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 31UL) != 31UL) throw r.Error("Missing Skin field");
            return value;
        }
        internal static TextureRecord ReadTexture(JsonInput r)
        {
            var value = new TextureRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "id": value.Id = r.String(); seen |= 1UL; break;
                    case "path": value.Path = r.String(); seen |= 2UL; break;
                    case "width": value.Width = r.UInt(); seen |= 4UL; break;
                    case "height": value.Height = r.UInt(); seen |= 8UL; break;
                    case "colorSpace": value.ColorSpace = ReadColorSpace(r); seen |= 16UL; break;
                    case "alphaMode": value.AlphaMode = ReadAlphaMode(r); seen |= 32UL; break;
                    case "minFilter": value.MinFilter = ReadFilter(r); seen |= 64UL; break;
                    case "magFilter": value.MagFilter = ReadFilter(r); seen |= 128UL; break;
                    case "mipmapFilter": value.MipmapFilter = (r.Present() ? (FilterRecord?)ReadFilter(r) : null); seen |= 256UL; break;
                    case "wrapU": value.WrapU = ReadWrap(r); seen |= 512UL; break;
                    case "wrapV": value.WrapV = ReadWrap(r); seen |= 1024UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 2047UL) != 2047UL) throw r.Error("Missing Texture field");
            return value;
        }
        internal static ColorSpaceRecord ReadColorSpace(JsonInput r)
        {
            return r.String() switch
            {
                "srgb" => ColorSpaceRecord.Srgb,
                "linear" => ColorSpaceRecord.Linear,
                _ => throw r.Error("Unknown ColorSpace")
            };
        }
        internal static AlphaModeRecord ReadAlphaMode(JsonInput r)
        {
            return r.String() switch
            {
                "straight" => AlphaModeRecord.Straight,
                "premultiplied" => AlphaModeRecord.Premultiplied,
                _ => throw r.Error("Unknown AlphaMode")
            };
        }
        internal static FilterRecord ReadFilter(JsonInput r)
        {
            return r.String() switch
            {
                "nearest" => FilterRecord.Nearest,
                "linear" => FilterRecord.Linear,
                _ => throw r.Error("Unknown Filter")
            };
        }
        internal static WrapRecord ReadWrap(JsonInput r)
        {
            return r.String() switch
            {
                "clamp" => WrapRecord.Clamp,
                "repeat" => WrapRecord.Repeat,
                "mirror" => WrapRecord.Mirror,
                _ => throw r.Error("Unknown Wrap")
            };
        }
        private static IkConstraintRecord ReadIkConstraint(JsonInput r)
        {
            var value = new IkConstraintRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "type": if (r.String() != "ik") throw r.Error("Invalid record type"); break;
                    case "id": value.Id = r.String(); seen |= 1UL; break;
                    case "name": value.Name = r.String(); seen |= 2UL; break;
                    case "skinRequired": value.SkinRequired = r.Boolean(); seen |= 4UL; break;
                    case "bones": value.Bones = r.List(x => x.UInt()); seen |= 8UL; break;
                    case "target": value.Target = r.UInt(); seen |= 16UL; break;
                    case "mix": value.Mix = r.Number(); seen |= 32UL; break;
                    case "bend": value.Bend = r.Number(); seen |= 64UL; break;
                    case "softness": value.Softness = r.Number(); seen |= 128UL; break;
                    case "compress": value.Compress = r.Boolean(); seen |= 256UL; break;
                    case "stretch": value.Stretch = r.Boolean(); seen |= 512UL; break;
                    case "scaleY": value.ScaleY = r.UInt(); seen |= 1024UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 2047UL) != 2047UL) throw r.Error("Missing IkConstraint field");
            return value;
        }
        private static TransformConstraintRecord ReadTransformConstraint(JsonInput r)
        {
            var value = new TransformConstraintRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "type": if (r.String() != "transform") throw r.Error("Invalid record type"); break;
                    case "id": value.Id = r.String(); seen |= 1UL; break;
                    case "name": value.Name = r.String(); seen |= 2UL; break;
                    case "skinRequired": value.SkinRequired = r.Boolean(); seen |= 4UL; break;
                    case "source": value.Source = r.UInt(); seen |= 8UL; break;
                    case "bones": value.Bones = r.List(x => x.UInt()); seen |= 16UL; break;
                    case "localSource": value.LocalSource = r.Boolean(); seen |= 32UL; break;
                    case "localTarget": value.LocalTarget = r.Boolean(); seen |= 64UL; break;
                    case "additive": value.Additive = r.Boolean(); seen |= 128UL; break;
                    case "offsets": value.Offsets = r.Tuple(6, x => x.Number()); seen |= 256UL; break;
                    case "mix": value.Mix = r.Tuple(6, x => x.Number()); seen |= 512UL; break;
                    case "mappings": value.Mappings = r.List(x => ReadMapping(x)); seen |= 1024UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 2047UL) != 2047UL) throw r.Error("Missing TransformConstraint field");
            return value;
        }
        private static PathConstraintRecord ReadPathConstraint(JsonInput r)
        {
            var value = new PathConstraintRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "type": if (r.String() != "path") throw r.Error("Invalid record type"); break;
                    case "id": value.Id = r.String(); seen |= 1UL; break;
                    case "name": value.Name = r.String(); seen |= 2UL; break;
                    case "skinRequired": value.SkinRequired = r.Boolean(); seen |= 4UL; break;
                    case "slot": value.Slot = r.UInt(); seen |= 8UL; break;
                    case "bones": value.Bones = r.List(x => x.UInt()); seen |= 16UL; break;
                    case "positionMode": value.PositionMode = r.UInt(); seen |= 32UL; break;
                    case "spacingMode": value.SpacingMode = r.UInt(); seen |= 64UL; break;
                    case "rotateMode": value.RotateMode = r.UInt(); seen |= 128UL; break;
                    case "position": value.Position = r.Number(); seen |= 256UL; break;
                    case "spacing": value.Spacing = r.Number(); seen |= 512UL; break;
                    case "mixRotate": value.MixRotate = r.Number(); seen |= 1024UL; break;
                    case "mixX": value.MixX = r.Number(); seen |= 2048UL; break;
                    case "mixY": value.MixY = r.Number(); seen |= 4096UL; break;
                    case "offsetRotation": value.OffsetRotation = r.Number(); seen |= 8192UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 16383UL) != 16383UL) throw r.Error("Missing PathConstraint field");
            return value;
        }
        private static PhysicsConstraintRecord ReadPhysicsConstraint(JsonInput r)
        {
            var value = new PhysicsConstraintRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "type": if (r.String() != "physics") throw r.Error("Invalid record type"); break;
                    case "id": value.Id = r.String(); seen |= 1UL; break;
                    case "name": value.Name = r.String(); seen |= 2UL; break;
                    case "skinRequired": value.SkinRequired = r.Boolean(); seen |= 4UL; break;
                    case "bone": value.Bone = r.UInt(); seen |= 8UL; break;
                    case "x": value.X = r.Number(); seen |= 16UL; break;
                    case "y": value.Y = r.Number(); seen |= 32UL; break;
                    case "rotate": value.Rotate = r.Number(); seen |= 64UL; break;
                    case "shearX": value.ShearX = r.Number(); seen |= 128UL; break;
                    case "scaleX": value.ScaleX = r.Number(); seen |= 256UL; break;
                    case "scaleY": value.ScaleY = r.UInt(); seen |= 512UL; break;
                    case "step": value.Step = r.Number(); seen |= 1024UL; break;
                    case "limit": value.Limit = r.Number(); seen |= 2048UL; break;
                    case "inertia": value.Inertia = r.Number(); seen |= 4096UL; break;
                    case "strength": value.Strength = r.Number(); seen |= 8192UL; break;
                    case "damping": value.Damping = r.Number(); seen |= 16384UL; break;
                    case "massInverse": value.MassInverse = r.Number(); seen |= 32768UL; break;
                    case "wind": value.Wind = r.Number(); seen |= 65536UL; break;
                    case "gravity": value.Gravity = r.Number(); seen |= 131072UL; break;
                    case "mix": value.Mix = r.Number(); seen |= 262144UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 524287UL) != 524287UL) throw r.Error("Missing PhysicsConstraint field");
            return value;
        }
        private static SliderConstraintRecord ReadSliderConstraint(JsonInput r)
        {
            var value = new SliderConstraintRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "type": if (r.String() != "slider") throw r.Error("Invalid record type"); break;
                    case "id": value.Id = r.String(); seen |= 1UL; break;
                    case "name": value.Name = r.String(); seen |= 2UL; break;
                    case "skinRequired": value.SkinRequired = r.Boolean(); seen |= 4UL; break;
                    case "animation": value.Animation = r.UInt(); seen |= 8UL; break;
                    case "time": value.Time = r.Number(); seen |= 16UL; break;
                    case "mix": value.Mix = r.Number(); seen |= 32UL; break;
                    case "additive": value.Additive = r.Boolean(); seen |= 64UL; break;
                    case "looped": value.Looped = r.Boolean(); seen |= 128UL; break;
                    case "source": value.Source = (r.Present() ? (SliderSourceRecord?)ReadSliderSource(r) : null); seen |= 256UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 511UL) != 511UL) throw r.Error("Missing SliderConstraint field");
            return value;
        }
        internal static ConstraintRecord ReadConstraint(JsonInput r)
        {
            return r.Discriminator() switch
            {
                "ik" => ReadIkConstraint(r),
                "transform" => ReadTransformConstraint(r),
                "path" => ReadPathConstraint(r),
                "physics" => ReadPhysicsConstraint(r),
                "slider" => ReadSliderConstraint(r),
                _ => throw r.Error("Unknown Constraint type")
            };
        }
        internal static MappingRecord ReadMapping(JsonInput r)
        {
            var value = new MappingRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "from": value.From = r.UInt(); seen |= 1UL; break;
                    case "to": value.To = r.UInt(); seen |= 2UL; break;
                    case "fromOffset": value.FromOffset = r.Number(); seen |= 4UL; break;
                    case "offset": value.Offset = r.Number(); seen |= 8UL; break;
                    case "scale": value.Scale = r.Number(); seen |= 16UL; break;
                    case "limit": value.Limit = (r.Present() ? (double?)r.Number() : null); seen |= 32UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 63UL) != 63UL) throw r.Error("Missing Mapping field");
            return value;
        }
        internal static SliderSourceRecord ReadSliderSource(JsonInput r)
        {
            var value = new SliderSourceRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "bone": value.Bone = r.UInt(); seen |= 1UL; break;
                    case "property": value.Property = r.UInt(); seen |= 2UL; break;
                    case "local": value.Local = r.Boolean(); seen |= 4UL; break;
                    case "fromOffset": value.FromOffset = r.Number(); seen |= 8UL; break;
                    case "offset": value.Offset = r.Number(); seen |= 16UL; break;
                    case "scale": value.Scale = r.Number(); seen |= 32UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 63UL) != 63UL) throw r.Error("Missing SliderSource field");
            return value;
        }
        internal static AnimationRecord ReadAnimation(JsonInput r)
        {
            var value = new AnimationRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "id": value.Id = r.String(); seen |= 1UL; break;
                    case "name": value.Name = r.String(); seen |= 2UL; break;
                    case "duration": value.Duration = r.Number(); seen |= 4UL; break;
                    case "timelines": value.Timelines = r.List(x => ReadTimeline(x)); seen |= 8UL; break;
                    case "deforms": value.Deforms = r.List(x => ReadDeform(x)); seen |= 16UL; break;
                    case "sequences": value.Sequences = r.List(x => ReadSequenceTimeline(x)); seen |= 32UL; break;
                    case "drawOrders": value.DrawOrders = r.List(x => ReadDrawOrder(x)); seen |= 64UL; break;
                    case "physicsResets": value.PhysicsResets = r.List(x => ReadPhysicsReset(x)); seen |= 128UL; break;
                    case "events": value.Events = r.List(x => ReadEvent(x)); seen |= 256UL; break;
                    case "skins": value.Skins = r.List(x => ReadSkinKey(x)); seen |= 512UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 511UL) != 511UL) throw r.Error("Missing Animation field");
            value.Skins ??= System.Array.Empty<SkinKeyRecord>();
            return value;
        }
        internal static uint[] ReadTimeline(JsonInput r)
        {
            return r.Tuple(5, x => x.UInt());
        }
        internal static DeformRecord ReadDeform(JsonInput r)
        {
            var value = new DeformRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "slot": value.Slot = r.UInt(); seen |= 1UL; break;
                    case "geometry": value.Geometry = r.UInt(); seen |= 2UL; break;
                    case "timing": value.Timing = ReadTimeline(r); seen |= 4UL; break;
                    case "frames": value.Frames = r.UInt(); seen |= 8UL; break;
                    case "vertices": value.Vertices = r.List(x => x.UInt()); seen |= 16UL; break;
                    case "offset": value.Offset = (r.Present() ? (DeformOffsetRecord?)ReadDeformOffset(r) : null); seen |= 32UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 63UL) != 63UL) throw r.Error("Missing Deform field");
            return value;
        }
        internal static DeformOffsetRecord ReadDeformOffset(JsonInput r)
        {
            var value = new DeformOffsetRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "timing": value.Timing = ReadTimeline(r); seen |= 1UL; break;
                    case "direction": value.Direction = r.UInt(); seen |= 2UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 3UL) != 3UL) throw r.Error("Missing DeformOffset field");
            return value;
        }
        internal static SequenceTimelineRecord ReadSequenceTimeline(JsonInput r)
        {
            var value = new SequenceTimelineRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "slot": value.Slot = r.UInt(); seen |= 1UL; break;
                    case "sequence": value.Sequence = r.UInt(); seen |= 2UL; break;
                    case "keys": value.Keys = r.List(x => ReadSequenceKey(x)); seen |= 4UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 7UL) != 7UL) throw r.Error("Missing SequenceTimeline field");
            return value;
        }
        internal static SequenceKeyRecord ReadSequenceKey(JsonInput r)
        {
            var value = new SequenceKeyRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "time": value.Time = r.Number(); seen |= 1UL; break;
                    case "index": value.Index = r.Integer(); seen |= 2UL; break;
                    case "mode": value.Mode = r.UInt(); seen |= 4UL; break;
                    case "delay": value.Delay = r.Number(); seen |= 8UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 15UL) != 15UL) throw r.Error("Missing SequenceKey field");
            return value;
        }
        internal static DrawOrderRecord ReadDrawOrder(JsonInput r)
        {
            var value = new DrawOrderRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "slots": value.Slots = r.List(x => x.UInt()); seen |= 1UL; break;
                    case "keys": value.Keys = r.List(x => ReadDrawKey(x)); seen |= 2UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 3UL) != 3UL) throw r.Error("Missing DrawOrder field");
            return value;
        }
        internal static DrawKeyRecord ReadDrawKey(JsonInput r)
        {
            var value = new DrawKeyRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "time": value.Time = r.Number(); seen |= 1UL; break;
                    case "order": value.Order = r.List(x => x.UInt()); seen |= 2UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 3UL) != 3UL) throw r.Error("Missing DrawKey field");
            return value;
        }
        internal static PhysicsResetRecord ReadPhysicsReset(JsonInput r)
        {
            var value = new PhysicsResetRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "time": value.Time = r.Number(); seen |= 1UL; break;
                    case "constraint": value.Constraint = (r.Present() ? (uint?)r.UInt() : null); seen |= 2UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 3UL) != 3UL) throw r.Error("Missing PhysicsReset field");
            return value;
        }
        internal static EventRecord ReadEvent(JsonInput r)
        {
            var value = new EventRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "time": value.Time = r.Number(); seen |= 1UL; break;
                    case "name": value.Name = r.String(); seen |= 2UL; break;
                    case "integer": value.Integer = r.Integer(); seen |= 4UL; break;
                    case "number": value.Number = r.Number(); seen |= 8UL; break;
                    case "text": value.Text = r.String(); seen |= 16UL; break;
                    case "audio": value.Audio = (r.Present() ? (string?)r.String() : null); seen |= 32UL; break;
                    case "volume": value.Volume = r.Number(); seen |= 64UL; break;
                    case "balance": value.Balance = r.Number(); seen |= 128UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 255UL) != 255UL) throw r.Error("Missing Event field");
            return value;
        }
        internal static SkinKeyRecord ReadSkinKey(JsonInput r)
        {
            var value = new SkinKeyRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "time": value.Time = r.Number(); seen |= 1UL; break;
                    case "skin": value.Skin = (r.Present() ? (uint?)r.UInt() : null); seen |= 2UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 3UL) != 3UL) throw r.Error("Missing SkinKey field");
            return value;
        }
        internal static ViewRecord ReadView(JsonInput r)
        {
            var value = new ViewRecord();
            ulong seen = 0;
            r.Expect('{');
            if (!r.Take('}')) do
            {
                string field = r.String();
                r.Expect(':');
                switch (field)
                {
                    case "type": value.Type = ReadType(r); seen |= 1UL; break;
                    case "encoding": value.Encoding = ReadEncoding(r); seen |= 2UL; break;
                    case "offset": value.Offset = r.UInt(); seen |= 4UL; break;
                    case "length": value.Length = r.UInt(); seen |= 8UL; break;
                    case "bytes": value.Bytes = r.UInt(); seen |= 16UL; break;
                    default: throw r.Error("Unknown field: " + field);
                }
            } while (r.More('}'));
            if ((seen & 31UL) != 31UL) throw r.Error("Missing View field");
            return value;
        }
        internal static TypeRecord ReadType(JsonInput r)
        {
            return r.String() switch
            {
                "f64" => TypeRecord.F64,
                "f32" => TypeRecord.F32,
                "u32" => TypeRecord.U32,
                _ => throw r.Error("Unknown Type")
            };
        }
        internal static EncodingRecord ReadEncoding(JsonInput r)
        {
            return r.String() switch
            {
                "f64" => EncodingRecord.F64,
                "f32" => EncodingRecord.F32,
                "u32" => EncodingRecord.U32,
                "u8" => EncodingRecord.U8,
                "u16" => EncodingRecord.U16,
                "packed" => EncodingRecord.Packed,
                "curves" => EncodingRecord.Curves,
                _ => throw r.Error("Unknown Encoding")
            };
        }
    }
}
