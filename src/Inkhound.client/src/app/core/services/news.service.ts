import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { PageResult } from './volume.service';

export type NewsCategory = 'Bd' | 'Manga' | 'Comics';
export type NewsFeed = 'TopSales' | 'Releases';
export type NewsEvolution = 'New' | 'Up' | 'Down' | 'Stable';

export const NEWS_CATEGORIES: { value: NewsCategory; label: string }[] = [
  { value: 'Bd',     label: 'BD' },
  { value: 'Manga',  label: 'Manga' },
  { value: 'Comics', label: 'Comics' }
];

export interface NewsLibraryLink { libraryId: string; volumeId: string; }
export interface NewsAuthor { name: string; role: string | null; }
export interface NewsImage { kind: 'Cover' | 'Plate' | 'Back'; thumbUrl: string; url: string; }

// Item d'un flux (top ventes ou nouveautés) — `library` renseigné si la série est déjà suivie.
export interface NewsItem {
  provider:       string;
  albumId:        string;
  seriesTitle:    string;
  albumNumber:    string | null;
  albumTitle:     string | null;
  seriesId:       string | null;
  publisher:      string | null;
  releaseDate:    string | null;   // yyyy-MM-dd
  category:       NewsCategory | null;
  description:    string | null;
  coverUrl:       string | null;
  coverLargeUrl:  string | null;
  albumUrl:       string | null;
  rank:           number | null;
  evolution:      NewsEvolution | null;
  evolutionDelta: number | null;
  weeksInChart:   number | null;
  enriched:       boolean;
  authors:        NewsAuthor[];
  library:        NewsLibraryLink | null;
}

export interface NewsTopSales { periods: string[]; period: string | null; items: NewsItem[]; }
export interface NewsReleases { months: string[]; page: PageResult<NewsItem>; }

export interface NewsAlbumEnrichment {
  albumId:       string;
  seriesId:      string;
  seriesTitle:   string;
  albumTitle:    string | null;
  albumNumber:   string | null;
  publisher:     string | null;
  collection:    string | null;
  year:          string | null;
  legalDeposit:  string | null;
  ean:           string | null;
  genre:         string | null;
  description:   string | null;
  rating:        number | null;
  ratingCount:   number | null;
  pages:         number | null;
  coverUrl:      string | null;
  coverLargeUrl: string | null;
  url:           string | null;
  authors:       NewsAuthor[];
  images:        NewsImage[];
}

export interface NewsSeriesAlbum {
  albumId: string; title: string; number: string | null; year: string | null; coverUrl: string | null; category: string;
}

export interface NewsSeriesDetail {
  seriesId:    string;
  title:       string;
  genre:       string | null;
  status:      string | null;
  albumCount:  number | null;
  origin:      string | null;
  language:    string | null;
  startYear:   string | null;
  endYear:     string | null;
  description: string | null;
  publisher:   string | null;
  coverUrl:    string | null;
  url:         string | null;
  albums:      NewsSeriesAlbum[];
}

export interface NewsAppearance { feed: NewsFeed; period: string; rank: number | null; }

export interface NewsAlbumDetail {
  item:        NewsItem | null;
  album:       NewsAlbumEnrichment;
  series:      NewsSeriesDetail | null;
  library:     NewsLibraryLink | null;
  appearances: NewsAppearance[];
}

/** Titre lisible d'un item : « Série #n — Titre ». */
export function newsItemLabel(item: { seriesTitle: string; albumNumber: string | null; albumTitle: string | null }): string {
  let label = item.seriesTitle;
  if (item.albumNumber) label += ` #${item.albumNumber}`;
  if (item.albumTitle && item.albumTitle !== item.seriesTitle) label += ` — ${item.albumTitle}`;
  return label;
}

@Injectable({ providedIn: 'root' })
export class NewsService {
  private http = inject(HttpClient);

  getTopSales(period?: string | null) {
    const params = period ? new HttpParams().set('period', period) : undefined;
    return this.http.get<NewsTopSales>('/api/news/top-sales', { params });
  }

  getReleases(category: NewsCategory | null, month: string | null, page: number, pageSize: number) {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (category) params = params.set('category', category);
    if (month) params = params.set('month', month);
    return this.http.get<NewsReleases>('/api/news/releases', { params });
  }

  getAlbum(albumId: string) {
    return this.http.get<NewsAlbumDetail>(`/api/news/albums/${albumId}`);
  }

  resolveSeries(albumId: string) {
    return this.http.post<{ seriesId: string; library: NewsLibraryLink | null }>(`/api/news/albums/${albumId}/resolve-series`, {});
  }

  refresh(force = true) {
    return this.http.post<{ jobId: string }>('/api/news/refresh', { force });
  }
}
