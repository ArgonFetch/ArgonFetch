import { Injectable } from '@angular/core';
import { environment } from '../../environments/environment';

export enum UrlType {
  Combined = 0,
  Media = 1
}

export interface StreamReferenceDto {
  bestQualityDescription?: string | null;
  bestQualityKey?: string | null;
  bestQualityFileExtension?: string | null;

  mediumQualityDescription?: string | null;
  mediumQualityKey?: string | null;
  mediumQualityFileExtension?: string | null;

  worstQualityDescription?: string | null;
  worstQualityKey?: string | null;
  worstQualityFileExtension?: string | null;

  urlType: UrlType;
}

@Injectable({
  providedIn: 'root'
})
export class ResourceUrlService {

  constructor() { }

  /**
   * Builds the complete URL for a resource based on the stream reference
   * @param streamRef The stream reference containing the key and type
   * @param quality The quality level to get URL for
   * @returns The complete URL for the resource
   */
  buildResourceUrl(streamRef: StreamReferenceDto | null | undefined, quality: 'best' | 'medium' | 'worst'): string | null {
    if (!streamRef) {
      return null;
    }

    let key: string | null | undefined = null;

    switch (quality) {
      case 'best':
        key = streamRef.bestQualityKey;
        break;
      case 'medium':
        key = streamRef.mediumQualityKey;
        break;
      case 'worst':
        key = streamRef.worstQualityKey;
        break;
    }

    if (!key) {
      return null;
    }

    // Build URL based on the type
    const baseUrl = environment.apiBaseUrl;
    const endpoint = streamRef.urlType === UrlType.Combined ? 'combined' : 'media';

    return `${baseUrl}/api/stream/${endpoint}/${key}`;
  }

  /**
   * Gets the file extension for a specific quality
   */
  getFileExtension(streamRef: StreamReferenceDto | null | undefined, quality: 'best' | 'medium' | 'worst'): string {
    if (!streamRef) {
      return '';
    }

    switch (quality) {
      case 'best':
        return streamRef.bestQualityFileExtension || '';
      case 'medium':
        return streamRef.mediumQualityFileExtension || '';
      case 'worst':
        return streamRef.worstQualityFileExtension || '';
      default:
        return '';
    }
  }

  /**
   * Checks if a quality level is available
   */
  hasQuality(streamRef: StreamReferenceDto | null | undefined, quality: 'best' | 'medium' | 'worst'): boolean {
    if (!streamRef) {
      return false;
    }

    switch (quality) {
      case 'best':
        return !!streamRef.bestQualityKey;
      case 'medium':
        return !!streamRef.mediumQualityKey;
      case 'worst':
        return !!streamRef.worstQualityKey;
      default:
        return false;
    }
  }
}
