export type EState = 'NOTINIT' | 'INVALID' | 'OK' | 'WARNING' | 'ERROR';
export type EValueType = 'STRING' | 'INT' | 'DOUBLE' | 'BOOL' | 'PASSWORD' | 'TEXT';

export interface OptionDefinition {
  id: string;
  name: string;
  value: string;
  valueType: EValueType;
  mandatory: boolean;
  description: string;
  regexValidator: string;
  defaultValue: string;
  serviceName: string;
}

export type ETraceLevel = 'INFO' | 'DEBUG' | 'WARNING' | 'ERROR' | 'CRITICAL' | 'NONE';
export type JobState = 'INITIALIZING' | 'RUNNING' | 'SUCCESS' | 'ERROR';

export interface StateService {
  state: EState;
  lastRefresh: string;
  serviceName: string;
  infos: string[];
}

export interface StateServiceManager {
  stateServices: StateService[];
  date: string;
  globalState: EState;
}

export interface Progression {
  total: number;
  completed: number;
  error: number;
  percentage: number;
}

export interface JobContext {
  jobId: string;
  state: JobState;
  title: string;
  progress: Progression;
  startDate: string;
  endDate: string | null;
  duration: string;
}

export interface TraceDefinition {
  message: string[];
  date: string;
  serviceName: string;
  jobId: string | null;
  level: ETraceLevel;
}

export interface UpdatedData {
  dataType: string;
  id: string;
  updatedAt: string;
}
