import { Component } from '@angular/core';

import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { 
  faYoutube, 
  faSpotify, 
  faSoundcloud, 
  faXTwitter,
  faInstagram, 
  faTiktok 
} from '@fortawesome/free-brands-svg-icons';
import { 
  faCheck,
  faLink,
  faPaste,
  faGears,
  faDownload
} from '@fortawesome/free-solid-svg-icons';

@Component({
  selector: 'app-about',
  templateUrl: './about.component.html',
  styleUrls: ['./about.component.scss'],
  standalone: true,
  imports: [FontAwesomeModule]
})
export class AboutComponent {
  // Font Awesome Icons
  faYoutube = faYoutube;
  faSpotify = faSpotify;
  faSoundcloud = faSoundcloud;
  faXTwitter = faXTwitter;
  faInstagram = faInstagram;
  faTiktok = faTiktok;
  faCheck = faCheck;

  platforms = [
    {
      name: 'YouTube',
      icon: this.faYoutube,
      features: [
        'Video downloads',
        'Audio extraction',
        'Playlist support',
        'Quality selection'
      ]
    },
    {
      name: 'Spotify',
      icon: this.faSpotify,
      features: [
        'Track downloads',
        'Playlist downloads',
        'Album downloads'
      ]
    },
    {
      name: 'SoundCloud',
      icon: this.faSoundcloud,
      features: [
        'Track downloads',
        'Playlist support',
        'High-quality audio'
      ]
    },
    {
      name: 'X',
      icon: this.faXTwitter,
      features: [
        'Video downloads',
        'GIF downloads',
        'Image downloads'
      ]
    },
    {
      name: 'Instagram',
      icon: this.faInstagram,
      features: [
        'Photo downloads',
        'Video downloads',
        'Story downloads',
        'Reel downloads'
      ]
    },
    {
      name: 'TikTok',
      icon: this.faTiktok,
      features: [
        'Video downloads',
        'No watermark option',
        'Audio extraction'
      ]
    }
  ];

  steps = [
    {
      title: 'Copy URL',
      description: 'Copy the URL of the media you want to download from any supported platform.',
      icon: faLink
    },
    {
      title: 'Paste & Download',
      description: 'Paste the URL into ArgonFetch and click the download button.',
      icon: faPaste
    },
    {
      title: 'Choose Format',
      description: 'Select your preferred format and quality options.',
      icon: faGears
    },
    {
      title: 'Get Your Media',
      description: 'Wait for the download to complete and enjoy your media!',
      icon: faDownload
    }
  ];
} 