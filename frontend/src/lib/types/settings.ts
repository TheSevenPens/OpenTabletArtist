export interface Settings {
  profiles: Profile[];
  lockUsableAreaDisplay: boolean;
  lockUsableAreaTablet: boolean;
}

export interface Profile {
  tablet: string;
  absoluteModeSettings: AbsoluteModeSettings;
  relativeModeSettings: RelativeModeSettings;
  bindingSettings: BindingSettings;
  outputMode: PluginRef | null;
  filters: PluginRef[];
}

export interface AbsoluteModeSettings {
  display: AreaSettings;
  tablet: AreaSettings;
  enableClipping: boolean;
  enableAreaLimiting: boolean;
  lockAspectRatio: boolean;
}

export interface AreaSettings {
  width: number;
  height: number;
  x: number;
  y: number;
  rotation: number;
}

export interface RelativeModeSettings {
  xSensitivity: number;
  ySensitivity: number;
  relativeRotation: number;
  resetTime: number;
}

export interface BindingSettings {
  tipActivationThreshold: number;
  tipButton: PluginRef | null;
  eraserActivationThreshold: number;
  eraserButton: PluginRef | null;
  penButtons: PluginRef[];
  auxButtons: PluginRef[];
}

export interface PluginRef {
  path: string;
  name: string;
  enable: boolean;
}
