/**
 * Full TypeScript typings for the JSON produced by Node Particle Editor export/save.
 *
 * Derived from:
 * - Core: packages/dev/core/src/Particles/Node (nodeParticleSystemSet.serialize,
 *   nodeParticleBlock.serialize, each Block's serialize/_deserialize, RegisterClass).
 * - NPE: nodeListComponent allBlocks, blockTools.GetBlockFromString, serializationTools,
 *   graphEditor editorData handling.
 *
 * Covers all block types that can appear in export (every RegisterClass in Particles/Node/Blocks)
 * and the exact shape of editorData, connection points, and block-specific fields.
 */

// ============== Editor data (canvas state) ==============

export interface IEditorDataLocation {
    blockId: number;
    x: number;
    y: number;
    isCollapsed: boolean;
}

export interface INodeParticleEditorData {
    locations: IEditorDataLocation[];
    frames: unknown[];
    x: number;
    y: number;
    zoom: number;
    /** Set when loading (map old id -> new id). */
    map?: Record<number, number>;
    [key: string]: unknown;
}

/** When loading, editorData can be legacy: an array of locations (no frames/x/y/zoom). GraphEditor normalizes to object. */
export type INodeParticleEditorDataOrLegacy = INodeParticleEditorData | IEditorDataLocation[];

// ============== Connection points (inputs/outputs) ==============

export interface ISerializedConnectionPoint {
    name: string;
    displayName?: string;
    valueType?: "number" | string;
    value?: number | number[];
    inputName?: string;
    targetBlockId?: number;
    targetConnectionName?: string;
    isExposedOnFrame?: boolean;
    exposedPortPosition?: number;
}

// ============== Base block (every block has this) ==============

export interface IBaseBlockSerialization {
    customType: string;
    id: number;
    name: string;
    visibleOnFrame: boolean;
    comments?: string;
    inputs: ISerializedConnectionPoint[];
    outputs: ISerializedConnectionPoint[];
}

// ============== Block variants (by customType) ==============

export interface ISystemBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.SystemBlock";
    capacity: number;
    manualEmitCount: number;
    blendMode: number;
    updateSpeed: number;
    preWarmCycles: number;
    preWarmStepOffset: number;
    isBillboardBased: boolean;
    billBoardMode: number;
    isLocal: boolean;
    disposeOnStop: boolean;
    doNoStart: boolean;
    renderingGroupId: number;
    startDelay: number;
    customShader: unknown | null;
}

export interface IParticleInputBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.ParticleInputBlock";
    type: number;
    contextualValue?: number;
    systemSource?: number;
    min: number;
    max: number;
    groupInInspector: string;
    displayInInspector: boolean;
    valueType?: "number" | string;
    value?: number | number[];
}

/**
 * Contextual Mode (contextualValue) → output type. ParticleInputBlock with contextualValue set
 * outputs this type; NPE menu "Contextual" blocks use these.
 *
 * Float     → Age, Lifetime, Angle, Age gradient, Size, Direction scale
 * Vector3   → Position, Direction, Scaled direction, Initial direction, Local position updated
 * Vector2   → Scale
 * Color4    → Color, Initial color, Color dead, Color step, Scaled color step
 * Int       → Sprite cell index, Sprite cell start, Sprite cell end
 */
export const CONTEXTUAL_VALUE_BY_OUTPUT_TYPE: Record<string, { name: string; value: number }[]> = {
    Float: [
        { name: "Age", value: 3 },
        { name: "Lifetime", value: 4 },
        { name: "Angle", value: 9 },
        { name: "Age gradient", value: 8 },
        { name: "Size", value: 25 },
        { name: "Direction scale", value: 32 },
    ],
    Vector3: [
        { name: "Position", value: 1 },
        { name: "Direction", value: 2 },
        { name: "Scaled direction", value: 6 },
        { name: "Initial direction", value: 21 },
        { name: "Local position updated", value: 24 },
    ],
    Vector2: [{ name: "Scale", value: 7 }],
    Color4: [
        { name: "Color", value: 5 },
        { name: "Initial color", value: 19 },
        { name: "Color dead", value: 20 },
        { name: "Color step", value: 22 },
        { name: "Scaled color step", value: 23 },
    ],
    Int: [
        { name: "Sprite cell index", value: 16 },
        { name: "Sprite cell start", value: 17 },
        { name: "Sprite cell end", value: 18 },
    ],
};

export interface IParticleTextureSourceBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.ParticleTextureSourceBlock";
    url?: string;
    serializedCachedData?: boolean;
    invertY?: boolean;
    textureDataUrl?: string;
}

export interface IParticleMathBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.ParticleMathBlock";
    operation: number;
}

export interface IParticleNumberMathBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.ParticleNumberMathBlock";
    operation: number;
}

export interface IParticleVectorMathBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.ParticleVectorMathBlock";
    operation: number;
}

export interface IParticleTrigonometryBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.ParticleTrigonometryBlock";
    operation: number;
}

export interface IParticleFloatToIntBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.ParticleFloatToIntBlock";
    operation: number;
}

export interface IParticleConditionBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.ParticleConditionBlock";
    test: number;
    epsilon?: number;
}

export interface IParticleRandomBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.ParticleRandomBlock";
    lockMode: number;
}

export interface IParticleLocalVariableBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.ParticleLocalVariableBlock";
    scope: number;
}

export interface IParticleGradientBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.ParticleGradientBlock";
    _entryCount: number;
}

export interface IParticleGradientValueBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.ParticleGradientValueBlock";
    reference: number;
}

export interface IParticleDebugBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.ParticleDebugBlock";
    stackSize: number;
}

export interface IParticleTriggerBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.ParticleTriggerBlock";
    limit: number;
    delay: number;
}

export interface IParticleTeleportOutBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.ParticleTeleportOutBlock";
    entryPoint: number | "";
}

export interface IAlignAngleBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.AlignAngleBlock";
    alignment: number;
}

export interface ISphereShapeBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.SphereShapeBlock";
    isHemispheric: boolean;
}

export interface IConeShapeBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.ConeShapeBlock";
    emitFromSpawnPointOnly: boolean;
}

export interface IMeshShapeBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.MeshShapeBlock";
    serializedCachedData?: boolean;
    cachedVertexData?: object;
    useMeshNormalsForDirection: boolean;
    useMeshColorForColor: boolean;
    worldSpace: boolean;
}

export interface ISetupSpriteSheetBlockSerialization extends IBaseBlockSerialization {
    customType: "BABYLON.SetupSpriteSheetBlock";
    width: number;
    height: number;
    start: number;
    end: number;
    spriteCellChangeSpeed: number;
    loop: boolean;
    randomStartCell: boolean;
}

/** Blocks that only have base fields (no extra props in serialize). */
export type IBaseOnlyBlockSerialization = IBaseBlockSerialization & {
    customType:
        | "BABYLON.BoxShapeBlock"
        | "BABYLON.PointShapeBlock"
        | "BABYLON.CylinderShapeBlock"
        | "BABYLON.CustomShapeBlock"
        | "BABYLON.UpdatePositionBlock"
        | "BABYLON.UpdateDirectionBlock"
        | "BABYLON.UpdateColorBlock"
        | "BABYLON.UpdateScaleBlock"
        | "BABYLON.UpdateSizeBlock"
        | "BABYLON.UpdateAngleBlock"
        | "BABYLON.UpdateAgeBlock"
        | "BABYLON.BasicPositionUpdateBlock"
        | "BABYLON.BasicColorUpdateBlock"
        | "BABYLON.BasicSpriteUpdateBlock"
        | "BABYLON.UpdateSpriteCellIndexBlock"
        | "BABYLON.UpdateFlowMapBlock"
        | "BABYLON.UpdateNoiseBlock"
        | "BABYLON.UpdateAttractorBlock"
        | "BABYLON.CreateParticleBlock"
        | "BABYLON.ParticleLerpBlock"
        | "BABYLON.ParticleClampBlock"
        | "BABYLON.ParticleNLerpBlock"
        | "BABYLON.ParticleSmoothStepBlock"
        | "BABYLON.ParticleStepBlock"
        | "BABYLON.ParticleConverterBlock"
        | "BABYLON.ParticleElbowBlock"
        | "BABYLON.ParticleTeleportInBlock"
        | "BABYLON.ParticleVectorLengthBlock";
};

/** Union of all block shapes in the export JSON. */
export type ExportBlock =
    | ISystemBlockSerialization
    | IParticleInputBlockSerialization
    | IParticleTextureSourceBlockSerialization
    | IParticleMathBlockSerialization
    | IParticleNumberMathBlockSerialization
    | IParticleVectorMathBlockSerialization
    | IParticleTrigonometryBlockSerialization
    | IParticleFloatToIntBlockSerialization
    | IParticleConditionBlockSerialization
    | IParticleRandomBlockSerialization
    | IParticleLocalVariableBlockSerialization
    | IParticleGradientBlockSerialization
    | IParticleGradientValueBlockSerialization
    | IParticleDebugBlockSerialization
    | IParticleTriggerBlockSerialization
    | IParticleTeleportOutBlockSerialization
    | IAlignAngleBlockSerialization
    | ISphereShapeBlockSerialization
    | IConeShapeBlockSerialization
    | IMeshShapeBlockSerialization
    | ISetupSpriteSheetBlockSerialization
    | IBaseOnlyBlockSerialization;

// ============== Root: full export/save JSON ==============

/**
 * Full typings for the JSON produced by export/save in Node Particle Editor.
 * Use this type for parsed result of save file or for building export payload.
 */
export interface INodeParticleEditorExportJson {
    tags: string[] | null;
    name: string;
    comment: string;
    editorData: INodeParticleEditorData;
    customType: "BABYLON.NodeParticleSystemSet";
    blocks: ExportBlock[];
}
