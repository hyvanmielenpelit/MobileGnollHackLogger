import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CollapsibleMarkdownComponent } from './collapsible-markdown.component';

describe('CollapsibleMarkdownComponent', () => {
  let component: CollapsibleMarkdownComponent;
  let fixture: ComponentFixture<CollapsibleMarkdownComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CollapsibleMarkdownComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(CollapsibleMarkdownComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should render nothing when text is empty or null', () => {
    component.text = null;
    fixture.detectChanges();

    const container = fixture.nativeElement.querySelector('.collapsible-markdown-container');
    expect(container).toBeNull();
  });

  it('should render markdown formatting and sanitize dangerous HTML/XSS', () => {
    component.text = '**Bold Header**\n\n- Bullet item 1\n- Bullet item 2\n\n`code_symbol`\n\n<script>alert("xss")</script><img src=x onerror="alert(1)">';
    fixture.detectChanges();

    const contentEl = fixture.nativeElement.querySelector('.markdown-content');
    expect(contentEl).toBeTruthy();
    expect(contentEl.querySelector('strong')?.textContent).toContain('Bold Header');
    expect(contentEl.querySelectorAll('li').length).toBe(2);
    expect(contentEl.querySelector('code')?.textContent).toContain('code_symbol');

    // Sanitization assertions
    expect(contentEl.querySelector('script')).toBeNull();
    expect(contentEl.innerHTML).not.toContain('onerror');
    expect(contentEl.innerHTML).not.toContain('<script');
  });

  it('should toggle expanded state and update aria-expanded and aria-label', () => {
    component.text = 'Line 1\n\nLine 2\n\nLine 3\n\nLine 4\n\nLine 5\n\nLine 6\n\nLine 7\n\nLine 8\n\nLine 9\n\nLine 10';
    component.label = 'expected answer criteria';
    component.collapsedMaxHeight = 10;
    component.isOverflowing = true;
    fixture.detectChanges();

    const toggleBtn = fixture.nativeElement.querySelector('.desc-expand-toggle');
    expect(toggleBtn).toBeTruthy();
    expect(toggleBtn.getAttribute('aria-expanded')).toBe('false');
    expect(toggleBtn.getAttribute('aria-label')).toBe('Show more of expected answer criteria');
    expect(toggleBtn.textContent.trim()).toBe('Show more');

    toggleBtn.click();
    fixture.detectChanges();

    expect(component.isExpanded).toBeTrue();
    expect(toggleBtn.getAttribute('aria-expanded')).toBe('true');
    expect(toggleBtn.getAttribute('aria-label')).toBe('Show less of expected answer criteria');
    expect(toggleBtn.textContent.trim()).toBe('Show less');
  });

  it('should apply collapsed class when isOverflowing is true and isExpanded is false', () => {
    component.text = 'Some long text that overflows.\n'.repeat(10);
    component.isOverflowing = true;
    component.isExpanded = false;
    fixture.detectChanges();

    const contentEl = fixture.nativeElement.querySelector('.markdown-content');
    expect(contentEl.classList.contains('collapsed')).toBeTrue();

    component.isExpanded = true;
    fixture.detectChanges();
    expect(contentEl.classList.contains('collapsed')).toBeFalse();
  });
});
