import { ChangeDetectionStrategy, Component, ElementRef, effect, inject, input } from '@angular/core';
import type { IconNode } from 'lucide';

// Lucide's own type for an icon, rather than a copy: a hand-written one drifted from theirs
// (their attribute values are optional, mine were not) and every icon failed to assign.
export type { IconNode } from 'lucide';

/**
 * Draws a Lucide icon.
 * <p>
 * Hand-rolled rather than pulled in from lucide-angular, which declares support for Angular 13
 * to 21 and refuses to install against this project's 22. The icon data is the framework
 * agnostic `lucide` package, which is only geometry, so the twenty lines below are the whole of
 * what the wrapper was doing for us.
 * <p>
 * The SVG is built as elements rather than assembled as markup, so nothing is ever handed to
 * innerHTML and there is no sanitiser to bypass.
 */
@Component({
  selector: 'app-icon',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
  template: '',
  styles: [`
    :host {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      // Inherits whatever the surrounding text is, which is what lets a button colour its own
      // icon by setting nothing more than its colour.
      color: inherit;
      line-height: 0;
    }
  `]
})
export class IconComponent {
  private static readonly SvgNamespace = 'http://www.w3.org/2000/svg';

  /** The icon to draw, imported from `lucide` by the component that shows it. */
  readonly name = input.required<IconNode>();

  /** Edge length in pixels. Lucide's own default is 24; ours is the size the UI mostly uses. */
  readonly size = input(18);

  /** How heavy the strokes are. Raised for the smaller sizes, where 2 looks faint. */
  readonly strokeWidth = input(2);

  private readonly host = inject(ElementRef<HTMLElement>);

  constructor() {
    effect(() => {
      const svg = document.createElementNS(IconComponent.SvgNamespace, 'svg');
      const size = this.size();

      // Lucide draws every icon on the same 24-unit grid, so one viewBox serves all of them.
      const attributes: Record<string, string> = {
        xmlns: IconComponent.SvgNamespace,
        width: `${size}`,
        height: `${size}`,
        viewBox: '0 0 24 24',
        fill: 'none',
        stroke: 'currentColor',
        'stroke-width': `${this.strokeWidth()}`,
        'stroke-linecap': 'round',
        'stroke-linejoin': 'round',
        'aria-hidden': 'true',
        focusable: 'false'
      };

      for (const [attribute, value] of Object.entries(attributes)) {
        svg.setAttribute(attribute, value);
      }

      for (const [tag, shape] of this.name()) {
        const element = document.createElementNS(IconComponent.SvgNamespace, tag);

        for (const [attribute, value] of Object.entries(shape)) {
          if (value !== undefined) {
            element.setAttribute(attribute, `${value}`);
          }
        }

        svg.appendChild(element);
      }

      // Replaced rather than appended: the icon can change while the component stays put, as
      // it does on the theme toggle.
      this.host.nativeElement.replaceChildren(svg);
    });
  }
}
