import { RuntimeErrorV1 } from "../errors.js";
import type { RuntimeConstraintV1, RuntimeCurvePropertyNameV1, RuntimeCurveV1 } from "../contracts.js";
import type { RuntimeDataV1 } from "../data.js";
import { mutableRuntimeSlotTintBytesV1 } from "../render/tint.js";
import { NO_CONTRIBUTION_V1, sampleContinuousKeysV1, sampleContinuousArrayKeysV1, sampleDiscreteKeysV1 } from "./curves.js";
import { sampleSequenceIndex } from "./sequence-clock.js";
import {
  createSamplingState, snapshotSamplingState, mergedAxisKeys, slotColorComponentsV1,
  sampledOptionalDarkColorExistsV1, resolveVertexSource, resolveDeformOwnerId, canonicalizeDeformV1,
  colorUnitByteV1, type RuntimeAnimationLayerV1, type RuntimeSampledPoseV1,
} from "./sampling.js";
import { AnimationWork, channelKey, checkedPoseNumber, applyChannelSample, transitionChannels,
  type PoseChannel, type PoseValue, type ChannelKind, type ChannelSample } from "./pose-channels.js";

export interface RuntimePoseEndpointV1 {
  readonly layer: RuntimeAnimationLayerV1 | null;
  readonly progress: number;
  readonly holdPreviousPose: boolean;
}
export type RuntimePoseTrackV1 = readonly RuntimePoseEndpointV1[];
type SamplingState = ReturnType<typeof createSamplingState>;
const BONE_FIELDS = ["x", "y", "rotation", "scaleX", "scaleY", "shearX", "shearY"] as const;
const REGION_FIELDS = ["x", "y", "rotation", "scaleX", "scaleY"] as const;
const COLOR_FIELDS = ["red", "green", "blue", "alpha", "darkRed", "darkGreen", "darkBlue"] as const;
const COLOR_PROPERTIES = ["color_r", "color_g", "color_b", "alpha", "dark_r", "dark_g", "dark_b"] as const;
const CONSTRAINT_FIELDS = {
  ik: ["targetX", "targetY", "mix", "softness", "bendPositive", "compress", "stretch"],
  transform: ["mixRotate", "mixX", "mixY", "mixScaleX", "mixScaleY", "mixShearY"],
  path: ["position", "spacing", "mixRotate", "mixX", "mixY"],
  physics: ["inertia", "strength", "damping", "mass", "wind", "gravity", "mix"],
  slider: ["sliderTime", "mix"],
} as const;
const properties: Readonly<Record<string, RuntimeCurvePropertyNameV1>> = {
  x: "x", y: "y", rotation: "rotation", scaleX: "scale_x", scaleY: "scale_y", shearX: "shear_x", shearY: "shear_y",
  targetX: "target_x", targetY: "target_y", mixRotate: "mix_rotate", mixX: "mix_x", mixY: "mix_y",
  mixScaleX: "mix_scale_x", mixScaleY: "mix_scale_y", mixShearY: "mix_shear_y", sliderTime: "slider_time",
};
const property = (field: string): RuntimeCurvePropertyNameV1 => properties[field] ?? field as RuntimeCurvePropertyNameV1;
const angular = (field: string): boolean => field === "rotation" || field === "shearX" || field === "shearY";
function member(value: object, field: string): unknown { return (value as Record<string, unknown>)[field]; }
function constraintValue(value: RuntimeConstraintV1, field: string): PoseValue {
  if ((field === "targetX" || field === "targetY") && value.type === "ik") return field === "targetX" ? value.target.x : value.target.y;
  return member(value, field === "sliderTime" ? "time" : field) as number | boolean;
}

class Projection {
  readonly schema = new Map<string, PoseChannel>();
  readonly initial = new Map<string, PoseValue>();
  private readonly publish = new Map<string, (value: PoseValue) => void>();
  readonly state: SamplingState;
  constructor(readonly data: RuntimeDataV1, skins: readonly string[], base: RuntimeSampledPoseV1 | null, readonly work: AnimationWork) {
    this.state = createSamplingState(data, skins, base);
    const add = (owner: string, id: string, field: string, kind: ChannelKind, setup: PoseValue, current: PoseValue, set: (value: PoseValue) => void, minimum?: number, maximum?: number): void => {
      work.use(1 + (kind === "vector" ? (setup as readonly number[]).length : 0));
      const key = channelKey(owner, id, field);
      this.schema.set(key, { kind, setup, ...(minimum === undefined ? {} : {minimum}), ...(maximum === undefined ? {} : {maximum}) });
      this.initial.set(key, current); this.publish.set(key, set);
    };
    for (const b of data.document.bones) {
      const current = this.state.bones.get(b.id)!;
      for (const field of BONE_FIELDS) add("bone", b.id, field, angular(field) ? "angle" : "scalar", b[field], current[field], v => { current[field] = v as number; });
      add("bone", b.id, "inherit", "choice", b.transformMode, current.transformMode, v => { current.transformMode = v as typeof current.transformMode; });
    }
    for (const s of data.document.slots) {
      const current = this.state.slots.get(s.id)!;
      add("slot", s.id, "attachment", "choice", s.attachmentId, current.attachmentId, v => { current.attachmentId = v as string | null; });
      const initialTint = mutableRuntimeSlotTintBytesV1(current), setupTint = mutableRuntimeSlotTintBytesV1({...s});
      for (let i = 0; i < 7; i += 1) {
        const field = COLOR_FIELDS[i]!;
        const get = (light: readonly number[], dark: readonly number[] | null, alpha: number): number => i === 3 ? alpha : Math.fround((i < 3 ? light[i]! : dark?.[i - 4] ?? 0) / 255);
        add("slot", s.id, field, "bounded", get(setupTint.lightRgb, setupTint.darkRgb, s.alpha), get(initialTint.lightRgb, initialTint.darkRgb, current.alpha), v => {
          if (i === 3) current.alpha = v as number;
          else if (i < 3) initialTint.lightRgb[i] = colorUnitByteV1(v as number);
          else initialTint.darkStorage[i - 4] = colorUnitByteV1(v as number);
        });
      }
      add("slot", s.id, "darkPresent", "choice", s.darkColor !== null, initialTint.darkRgb !== null, v => { initialTint.darkRgb = v ? initialTint.darkStorage : null; });
    }
    const attachments = this.state.attachmentById;
    for (const a of data.document.attachments) {
      if (a.type === "region") {
        const current = this.state.regions.get(a.id)!;
        for (const field of REGION_FIELDS) add("attachment", a.id, field, angular(field) ? "angle" : "scalar", a[field], current[field], v => { current[field] = v as number; });
      }
      if ((a.type === "region" || a.type === "mesh") && a.sequence !== null && a.sequence.imageIds.length > 0) {
        const setup = Math.max(0, Math.min(a.sequence.imageIds.length - 1, a.sequence.setupIndex));
        add("attachment", a.id, "sequence", "choice", setup, this.state.sequenceIndices.get(a.id) ?? setup, v => { this.state.sequenceIndices.set(a.id, v as number); });
      }
      if (a.type === "region" || a.type === "point") continue;
      const owner = a.type === "mesh" ? resolveDeformOwnerId(a, attachments) : a.id;
      if (owner !== a.id) continue;
      const source = resolveVertexSource(a, attachments), weighted = source.weights.length > 0;
      const setup = weighted ? new Array<number>(source.weights.reduce((n, list) => n + list.length * 2, 0)).fill(0) : [...source.vertices];
      add("attachment", a.id, "deform", "vector", setup, this.state.deforms.get(owner)?.values ?? setup, v => {
        this.state.deforms.set(owner, {weighted, values: v as readonly number[]});
        this.state.deformEpochByOwner.set(owner, this.state.deformEpoch);
      });
    }
    for (const c of data.document.constraints) {
      const current = this.state.constraints.get(c.id)!;
      for (const field of CONSTRAINT_FIELDS[c.type]) {
        const setup = constraintValue(c, field);
        const kind = typeof setup === "boolean" ? "choice" : c.type === "physics" && ["mix", "inertia", "damping", "mass"].includes(field) ? "bounded" : "scalar";
        add("constraint", c.id, field, kind, setup, constraintValue(current, field), v => {
          if ((field === "targetX" || field === "targetY") && current.type === "ik") (current.target as {x:number;y:number})[field === "targetX" ? "x" : "y"] = v as number;
          else (current as unknown as Record<string, unknown>)[field === "sliderTime" ? "time" : field] = v;
        }, field === "mass" ? Math.pow(2, -149) : undefined, field === "mass" ? 3.4028234663852886e38 : undefined);
      }
    }
    add("skeleton", "", "skins", "choice", [], skins, v => { this.state.sampledSkinIds = [...v as readonly string[]]; });
    add("skeleton", "", "order", "order", this.state.setupOrder, this.state.drawOrder, v => { this.state.drawOrder = [...v as readonly string[]]; });
    add("skeleton", "", "orderActive", "choice", false, this.state.drawOrderSampled, v => { this.state.drawOrderSampled = v as boolean; });
  }
  finish(values: ReadonlyMap<string, PoseValue>): RuntimeSampledPoseV1 {
    this.work.use(values.size);
    for (const [key, value] of values) this.publish.get(key)!(value);
    return snapshotSamplingState(this.data, this.state);
  }
}

function sampleEndpoint(projection: Projection, layer: RuntimeAnimationLayerV1 | null): ChannelSample {
  const values = new Map<string, PoseValue>(), orderPatches: string[][] = [];
  if (layer === null) return {values, orderPatches, weight: 0, additive: false};
  const {animation, sampleTime: time, forceStepped} = layer, transparent = layer.alpha === 0;
  const work = projection.work;
  const put = (owner: string, id: string, field: string, value: PoseValue): void => {
    work.use(); const key = channelKey(owner, id, field), channel = projection.schema.get(key);
    if (channel === undefined) throw new Error("Animation channel has no setup value.");
    values.set(key, transparent ? channel.setup : value);
  };
  const continuous = <T extends {readonly time:number;readonly curve:RuntimeCurveV1}>(keys: readonly T[] | null, field: string, get: (key:T)=>number, owner: string, id: string, outputField = field): void => {
    if (keys === null) return;
    work.use(keys.length);
    if (!keys.some(key => key.time <= time)) return;
    if (transparent) { put(owner,id,outputField,0); return; }
    try {
      const value = sampleContinuousKeysV1(keys,time,property(field),get,{angular:angular(field),forceStepped});
      if (value !== NO_CONTRIBUTION_V1) put(owner,id,outputField,checkedPoseNumber(value));
    } catch (error) {
      if (error instanceof RuntimeErrorV1 && error.operation === "sampleCurve") throw new RuntimeErrorV1(error.runtimeName, "apply", error.message, {field: "animationCurve", entityId: id, cause: error});
      throw error;
    }
  };
  const discrete = <T extends {readonly time:number}>(keys: readonly T[] | null, get:(key:T)=>PoseValue, owner:string,id:string,field:string): void => {
    if (keys === null) return; work.use(keys.length);
    if (transparent) { if (keys.some(k => k.time <= time)) put(owner,id,field,null); return; }
    const value = sampleDiscreteKeysV1(keys,time,get); if (value !== NO_CONTRIBUTION_V1) put(owner,id,field,value);
  };
  for (const t of animation.boneTimelines) {
    discrete(t.inherit,k=>k.inherit,"bone",t.boneId,"inherit");
    continuous(t.rotate,"rotation",k=>k.angle,"bone",t.boneId);
    for (const [field,pair,single] of [["x",t.translate,t.translateX],["y",t.translate,t.translateY],["scaleX",t.scale,t.scaleX],["scaleY",t.scale,t.scaleY],["shearX",t.shear,t.shearX],["shearY",t.shear,t.shearY]] as const) {
      const axis = field === "y" || field.endsWith("Y") ? "y" : "x";
      continuous(mergedAxisKeys(t,field,pair,single,axis,property(field)),field,k=>k.value,"bone",t.boneId);
    }
  }
  for (const t of animation.slotTimelines) {
    if (layer.attachmentsAllowed) discrete(t.attachment,k=>k.attachmentId,"slot",t.slotId,"attachment");
    const separateAlpha = (t.alpha?.length ?? 0) > 0;
    if (t.color !== null && t.color.some(k=>k.time<=time)) {
      for (let i=0;i<7;i+=1) {
        if (i===3 && separateAlpha) continue;
        if (transparent) { put("slot",t.slotId,COLOR_FIELDS[i]!,0); continue; }
        const value = sampleContinuousKeysV1(t.color,time,COLOR_PROPERTIES[i]!,k=>slotColorComponentsV1(k)[i]!,{forceStepped});
        work.use(t.color.length);
        if (value !== NO_CONTRIBUTION_V1) put("slot",t.slotId,COLOR_FIELDS[i]!,i===3 ? value : Math.fround(colorUnitByteV1(value)/255));
      }
      put("slot",t.slotId,"darkPresent",transparent ? false : sampledOptionalDarkColorExistsV1(t.color,time));
    }
    if (separateAlpha) continuous(t.alpha,"alpha",k=>k.alpha,"slot",t.slotId);
  }
  for (const t of animation.attachmentTimelines) {
    const a=projection.data.attachment(t.attachmentId);
    for (const field of REGION_FIELDS) continuous(t.region,field,k=>k[field],"attachment",t.attachmentId);
    if (t.deform !== null) {
      work.use(t.deform.reduce((n,k)=>n+1+k.vertices.length,0));
      if (t.deform.some(k=>k.time<=time)) {
        if (transparent) put("attachment",a.id,"deform",[]);
        else {
          if (a.type === "region" || a.type === "point" || (a.type === "mesh" && resolveDeformOwnerId(a,projection.state.attachmentById)!==a.id)) throw new Error("Invalid deformation owner.");
          const source=resolveVertexSource(a,projection.state.attachmentById), sampled:number[]=[], canonical:number[]=[];
          sampled.length=t.deform[0]?.vertices.length ?? 0;
          if (sampleContinuousArrayKeysV1(t.deform,time,k=>k.vertices,sampled,"x","y",forceStepped)) {
            canonicalizeDeformV1(source,t.deformSpace,sampled,canonical); put("attachment",a.id,"deform",canonical);
          }
        }
      }
    }
    if (layer.attachmentsAllowed && t.sequence !== null && (a.type === "region" || a.type === "mesh") && a.sequence !== null) {
      work.use(t.sequence.length);
      if (transparent) { if (t.sequence.some(k=>k.time<=time)) put("attachment",a.id,"sequence",0); }
      else {
        const k=sampleDiscreteKeysV1(t.sequence,time,k=>k);
        if(k!==NO_CONTRIBUTION_V1){ const value=sampleSequenceIndex(time,k.time,k.delay,a.sequence.imageIds.length,k.index,k.mode); if(value===null)throw new Error("Invalid sequence clock.");put("attachment",a.id,"sequence",value); }
      }
    }
  }
  for (const t of animation.constraintTimelines) {
    const wildcard=t.type==="physics" && t.constraintId==="*";
    for(const c of projection.data.document.constraints){
      if(c.type!==t.type || (!wildcard && c.id!==t.constraintId))continue;
      for(const field of CONSTRAINT_FIELDS[c.type]){
        if(wildcard && member(c,`${field}Global`)!==true)continue;
        const keys=t.keys as unknown as readonly ({readonly time:number;readonly curve:RuntimeCurveV1}&Record<string,unknown>)[];
        if(typeof constraintValue(c,field)==="boolean"){
          discrete(keys.filter(k=>typeof k[field]==="boolean"),k=>k[field] as boolean,"constraint",c.id,field);
        }else continuous(keys.filter(k=>typeof k[field]==="number" && Number.isFinite(k[field])),field,k=>k[field] as number,"constraint",c.id);
      }
    }
  }
  if(layer.attachmentsAllowed)discrete(animation.skins,k=>k.skinId===null?[]:[k.skinId],"skeleton","","skins");
  if(layer.drawOrderAllowed){
    for(const keys of [animation.drawOrder,...animation.drawOrderFolders.map(f=>f.keys)]){
      work.use(keys.length);
      if(transparent){if(keys.some(k=>k.time<=time)){put("skeleton","","order",[]);put("skeleton","","orderActive",true);}continue;}
      const value=sampleDiscreteKeysV1(keys,time,k=>k.slotIds);
      if(value!==NO_CONTRIBUTION_V1){orderPatches.push([...value]);put("skeleton","","order",[]);put("skeleton","","orderActive",true);}
    }
  }
  return {values,orderPatches,weight:layer.alpha,additive:layer.blend==="additive"};
}

export function sampleAnimationTracksV1(data:RuntimeDataV1,tracks:readonly RuntimePoseTrackV1[],skins:readonly string[]):RuntimeSampledPoseV1 {
  const work=new AnimationWork(), projection=new Projection(data,skins,null,work);
  let pose=projection.initial;
  for(const track of tracks){
    const underlay=pose;let outgoing=new Map(underlay),first=true;
    for(const endpoint of track){
      work.use();
      if(!first && endpoint.progress===0)continue;
      const sample=sampleEndpoint(projection,endpoint.layer),incoming=applyChannelSample(projection.schema,underlay,sample,work);
      outgoing=first || endpoint.progress===1 ? incoming : transitionChannels(projection.schema,outgoing,incoming,sample,endpoint.progress,endpoint.holdPreviousPose,work);
      first=false;
    }
    pose=outgoing;
  }
  return projection.finish(pose);
}
export function applyEndpointLayerV1(data:RuntimeDataV1,pose:RuntimeSampledPoseV1,layer:RuntimeAnimationLayerV1):RuntimeSampledPoseV1 {
  const work=new AnimationWork(),projection=new Projection(data,pose.sampledSkinIds,pose,work);
  return projection.finish(applyChannelSample(projection.schema,projection.initial,sampleEndpoint(projection,layer),work));
}
