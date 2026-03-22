export interface TabletInfo {
  name: string;
  specifications: TabletSpecifications;
}

export interface TabletSpecifications {
  digitizer: DigitizerSpecs;
  pen: PenSpecs;
  auxiliaryButtons?: ButtonSpecs;
  mouseButtons?: ButtonSpecs;
  touch?: DigitizerSpecs;
}

export interface DigitizerSpecs {
  width: number;
  height: number;
  maxX: number;
  maxY: number;
}

export interface PenSpecs {
  maxPressure: number;
  buttonCount: number;
}

export interface ButtonSpecs {
  buttonCount: number;
}
