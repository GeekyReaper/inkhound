import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface ProxyInfo {
  address: string;
  port: number;
  countryCode: string | null;
  valid: boolean;
  failed: boolean;
  isActive: boolean;
}

export interface WebshareProxyActivity {
  requestCount: number;
  bytes: number;
  errorCount: number;
}

export interface WebshareStatistics {
  email: string | null;
  firstName: string | null;
  lastName: string | null;
  planType: string | null;
  planSubtype: string | null;
  proxyCount: number | null;
  bandwidthLimitGb: number | null;
  term: string | null;
  periodStart: string | null;
  periodEnd: string | null;
  requestCount: number;
  totalBytes: number;
  errorCount: number;
  averageDurationSeconds: number;
  totalBytesMb: number;
  totalBytesGb: number;
  bandwidthUsagePercent: number | null;
  byProxy: Record<string, WebshareProxyActivity> | null;
  byDomain: Record<string, number> | null;
}

@Injectable({ providedIn: 'root' })
export class ProxyService {
  private http = inject(HttpClient);

  getProxies() {
    return this.http.get<ProxyInfo[]>('/api/webshareproxy/proxies');
  }

  nextProxy() {
    return this.http.post<ProxyInfo>('/api/webshareproxy/next', null);
  }

  getStatistics() {
    return this.http.get<WebshareStatistics>('/api/webshareproxy/statistics');
  }

  getServicesUsingProxy() {
    return this.http.get<string[]>('/api/webshareproxy/services-using-proxy');
  }
}
