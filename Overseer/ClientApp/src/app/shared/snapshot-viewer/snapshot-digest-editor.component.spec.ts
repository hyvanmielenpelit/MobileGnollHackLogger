import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DIGEST_MAX_CHARS, SnapshotDigestEditorComponent } from './snapshot-digest-editor.component';

describe('SnapshotDigestEditorComponent', () => {
  let fixture: ComponentFixture<SnapshotDigestEditorComponent>;
  let component: SnapshotDigestEditorComponent;
  let host: HTMLElement;

  const digest = 'Board digest (extract of the snapshot):\nStatus:\nHP:31(44)';

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SnapshotDigestEditorComponent]
    }).compileComponents();
  });

  /* CodeMirror is loaded with import(), so every test waits for ready before touching it. */
  async function mount(text: string) {
    fixture = TestBed.createComponent(SnapshotDigestEditorComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('text', text);
    fixture.detectChanges();
    await component.ready;
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  }

  function regenerateButton(): HTMLButtonElement {
    return host.querySelector<HTMLButtonElement>('.regenerate-btn')!;
  }

  function counterText(): string {
    return (host.querySelector('.digest-counter')?.textContent ?? '').trim();
  }

  it('shows the digest in the editor and counts it against the cap', async () => {
    await mount(digest);

    expect(component.loadError).toBeFalse();
    expect(host.querySelector('.cm-editor')).toBeTruthy();
    expect(component.view!.state.doc.toString()).toBe(digest);
    expect(counterText()).toBe(`${digest.length.toLocaleString('en-US')} / 6,000 chars`);
    expect(host.querySelector('.digest-cap-warning')).toBeNull();
  });

  it('emits the whole document on every change', async () => {
    await mount(digest);
    const emitted: string[] = [];
    component.textChange.subscribe(value => emitted.push(value));

    component.view!.dispatch({ changes: { from: 0, insert: 'X' } });
    fixture.detectChanges();

    expect(emitted.length).toBe(1);
    expect(emitted[0]).toBe('X' + digest);
    expect(counterText()).toBe(`${(digest.length + 1).toLocaleString('en-US')} / 6,000 chars`);
  });

  it('replaces the document when the text is set from outside', async () => {
    await mount(digest);
    const rebuilt = 'A rebuilt digest.';

    fixture.componentRef.setInput('text', rebuilt);
    fixture.detectChanges();

    expect(component.view!.state.doc.toString()).toBe(rebuilt);
    expect(counterText()).toBe(`${rebuilt.length.toLocaleString('en-US')} / 6,000 chars`);
  });

  it('warns once the digest is past the cap', async () => {
    await mount(digest);

    component.view!.dispatch({ changes: { from: 0, insert: 'x'.repeat(DIGEST_MAX_CHARS) } });
    fixture.detectChanges();

    expect(component.overCap).toBeTrue();
    expect(host.querySelector('.digest-counter.over-cap')).toBeTruthy();
    expect(host.querySelector('.digest-cap-warning')!.textContent)
      .toContain('the server cuts the digest at 6,000 characters');
  });

  it('names the regenerate action and refuses a click while a request is pending', async () => {
    await mount(digest);
    let regenerated = 0;
    component.regenerate.subscribe(() => regenerated++);

    expect(regenerateButton().getAttribute('aria-label')).toBe('Regenerate digest from snapshot');
    expect(regenerateButton().getAttribute('aria-disabled')).toBeNull();

    fixture.componentRef.setInput('regenerating', true);
    fixture.detectChanges();
    expect(regenerateButton().getAttribute('aria-disabled')).toBe('true');
    expect(regenerateButton().getAttribute('aria-busy')).toBe('true');
    expect(regenerateButton().querySelector('.gh-spinner-small')).toBeTruthy();

    regenerateButton().click();
    expect(regenerated).toBe(0);

    fixture.componentRef.setInput('regenerating', false);
    fixture.detectChanges();
    regenerateButton().click();
    expect(regenerated).toBe(1);
  });
});
