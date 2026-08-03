import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { LanguageService } from '../core/language.service';

/**
 * Persistent medical disclaimer (FR-8.7). Present on every screen — the product presents
 * information, never a diagnosis (§17.1).
 */
@Component({
  selector: 'mt-disclaimer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="border-t border-slate-200 px-6 py-4 text-center text-xs leading-relaxed text-slate-500">
      @if (language.current() === 'ta') {
        MediTrail ஒரு தகவல் கருவி மட்டுமே. இது நோயறிதல் அல்ல. மருந்துகள் குறித்த எந்த முடிவையும்
        எடுப்பதற்கு முன் மருத்துவர் அல்லது மருந்தாளுநரை அணுகவும்.
      } @else {
        MediTrail is an information tool, not a diagnosis. It never recommends starting, stopping or
        changing a medication. Always confirm findings with a doctor or pharmacist.
      }
    </p>
  `
})
export class DisclaimerComponent {
  protected readonly language = inject(LanguageService);
}
