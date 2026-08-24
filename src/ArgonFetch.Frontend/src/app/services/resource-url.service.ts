import { Injectable } from '@angular/core';
import { environment } from '../../environments/environment';
import { MediaRenditionDto, StreamReferenceDto, UrlType } from '../api';

// StreamReferenceDto and UrlType come from the generated API client rather than being
// redefined here - the local copies had drifted from the generated ones, which is why
// callers had to cast through `any` to pass a DTO into this service.
export type { StreamReferenceDto, MediaRenditionDto };
export { UrlType };

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
   * Builds the URL for one rendition. A rendition the server marked as converted carries the
   * target container, which the stream endpoint takes as its format parameter.
   */
  buildRenditionUrl(rendition: MediaRenditionDto | null | undefined): string | null {
    if (!rendition?.key) {
      return null;
    }

    const endpoint = rendition.urlType === UrlType.Combined ? 'combined' : 'media';
    const url = `${environment.apiBaseUrl}/api/stream/${endpoint}/${rendition.key}`;

    return rendition.convertTo ? `${url}?format=${rendition.convertTo}` : url;
  }

  /**
   * Human-readable transfer size, or an empty string when the source does not report one.
   * Shown next to a rendition because size is what tells someone how long it will take.
   */
  formatSize(bytes: number | null | undefined): string {
    if (!bytes || bytes <= 0) {
      return '';
    }

    const megabytes = bytes / (1024 * 1024);

    return megabytes >= 1024
      ? `${(megabytes / 1024).toFixed(1)} GB`
      : `${megabytes.toFixed(megabytes < 10 ? 1 : 0)} MB`;
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
