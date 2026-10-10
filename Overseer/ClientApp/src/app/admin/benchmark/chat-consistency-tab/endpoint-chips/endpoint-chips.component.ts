import { ChangeDetectionStrategy, Component, Input, OnChanges } from '@angular/core';

import {
  CcEndpointStatus,
  ccEndpointBriefStatus,
  ccEndpointBriefStatusText,
  ccEndpointStatus,
  ccEndpointStatusText
} from '../chat-consistency-results';
import { CcEndpointBrief, CcEndpointResult } from '../chat-consistency.models';

/** One static endpoint chip. */
export interface CcEndpointChip {
  id: string;
  name: string;
  status: CcEndpointStatus;
  /** `Within margin`, `More work`. */
  text: string;
}

/** A full result's endpoint carries its direction; a summary's brief does not. */
function isResult(endpoint: CcEndpointBrief | CcEndpointResult): endpoint is CcEndpointResult {
  return 'direction' in endpoint;
}

/** The chips of the given endpoints, in their order. */
export function ccEndpointChips(endpoints: readonly (CcEndpointBrief | CcEndpointResult)[] | null | undefined): CcEndpointChip[] {
  return (endpoints ?? []).map(endpoint => isResult(endpoint)
    ? { id: endpoint.id, name: endpoint.name, status: ccEndpointStatus(endpoint), text: ccEndpointStatusText(endpoint) }
    : { id: endpoint.id, name: endpoint.name, status: ccEndpointBriefStatus(endpoint), text: ccEndpointBriefStatusText(endpoint) });
}

/**
 * Each endpoint's status as a static chip: a status icon, the endpoint id and the status word, never
 * the icon alone; the endpoint's name is read but not shown. Reads a saved analysis's summary
 * endpoints or a full result's. Renders nothing for no endpoints.
 */
@Component({
  selector: 'app-cc-endpoint-chips',
  standalone: true,
  templateUrl: './endpoint-chips.component.html',
  styleUrls: ['./endpoint-chips.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcEndpointChipsComponent implements OnChanges {
  @Input() endpoints: readonly (CcEndpointBrief | CcEndpointResult)[] | null | undefined = [];
  /** The list's accessible name, distinct per list where several share a page. */
  @Input() label = 'Endpoints';

  chips: CcEndpointChip[] = [];

  ngOnChanges(): void {
    this.chips = ccEndpointChips(this.endpoints);
  }
}
