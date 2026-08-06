var T=class extends Event{oldState;newState;constructor(e,t={}){let o=Object.assign({},t);delete o.oldState,delete o.newState,super(e,o),this.oldState=String(t.oldState||``),this.newState=String(t.newState||``)}};var _=new WeakMap;function U(e,t,o){_.set(e,setTimeout(()=>{_.has(e)&&e.dispatchEvent(new T(`toggle`,{cancelable:!1,oldState:t,newState:o}))},0))}var N=globalThis.ShadowRoot||function(){};var K=globalThis.HTMLDialogElement||function(){};var A=new WeakMap;var d=new WeakMap;var c=new WeakMap;var y=new WeakMap;function P(e){return y.get(e)||`hidden`}var M=new WeakMap;function m(e){return[...e].pop()}function Q(e){let t=e.popoverTargetElement;if(!(t instanceof HTMLElement))return;let o=P(t);e.popoverTargetAction===`show`&&o===`showing`||e.popoverTargetAction===`hide`&&o===`hidden`||(o===`showing`?S(t,!0,!0):v(t,!1)&&(M.set(t,e),I(t)))}function v(e,t){return!(e.popover!==`auto`&&e.popover!==`manual`&&e.popover!==`hint`||!e.isConnected||t&&P(e)!==`showing`||!t&&P(e)!==`hidden`||e instanceof K&&e.hasAttribute(`open`)||document.fullscreenElement===e)}function q(e){if(!e)return 0;let t=d.get(document)||new Set,o=c.get(document)||new Set;return o.has(e)?[...o].indexOf(e)+t.size+1:t.has(e)?[...t].indexOf(e)+1:0}function J(e){let t=Y(e),o=X(e);return q(t)>q(o)?t:o}function L(e){let t,o=c.get(e)||new Set,n=d.get(e)||new Set,i=o.size>0?o:n.size>0?n:null;return i?(t=m(i),t.isConnected?t:(i.delete(t),L(e))):null}function j(e){for(let t of e||[])if(!t.isConnected)e.delete(t);else return t;return null}function b(e){return typeof e.getRootNode==`function`?e.getRootNode():e.parentNode?b(e.parentNode):e}function Y(e){for(;e;){if(e instanceof HTMLElement&&e.popover===`auto`&&y.get(e)===`showing`)return e;if(e=e instanceof Element&&e.assignedSlot||e.parentElement||b(e),e instanceof N&&(e=e.host),e instanceof Document)return}}function X(e){for(;e;){let t=e.popoverTargetElement;if(t instanceof HTMLElement)return t;if(e=e.parentElement||b(e),e instanceof N&&(e=e.host),e instanceof Document)return}}function z(e,t){let o=new Map,n=0;for(let l of t||[])o.set(l,n),n+=1;o.set(e,n),n+=1;let i=null;function a(l){if(!l)return;let p=!1,u=null;for(;!p;){if(u=Y(l)||null,u===null||!o.has(u))return;(e.popover===`hint`||u.popover===`auto`)&&(p=!0),p||(l=u.parentElement)}let x=o.get(u)??null;(i===null||o.get(i)<x)&&(i=u)}return a(e.parentElement||b(e)),i}function Z(e){return e.hidden||e instanceof N||(e instanceof HTMLButtonElement||e instanceof HTMLInputElement||e instanceof HTMLSelectElement||e instanceof HTMLTextAreaElement||e instanceof HTMLOptGroupElement||e instanceof HTMLOptionElement||e instanceof HTMLFieldSetElement)&&e.disabled||e instanceof HTMLInputElement&&e.type===`hidden`||e instanceof HTMLAnchorElement&&e.href===``?!1:typeof e.tabIndex==`number`&&e.tabIndex!==-1}function ee(e){if(e.shadowRoot&&e.shadowRoot.delegatesFocus!==!0)return null;let t=e;t.shadowRoot&&(t=t.shadowRoot);let o=t.querySelector(`[autofocus]`);if(o)return o;{let a=t.querySelectorAll(`slot`);for(let l of a){let p=l.assignedElements({flatten:!0});for(let u of p){if(u.hasAttribute(`autofocus`))return u;if(o=u.querySelector(`[autofocus]`),o)return o}}}let n=e.ownerDocument.createTreeWalker(t,NodeFilter.SHOW_ELEMENT),i=n.currentNode;for(;i;){if(Z(i))return i;i=n.nextNode()}}function te(e){var t;(t=ee(e))==null||t.focus()}var k=new WeakMap;function I(e){if(!v(e,!1))return;let t=e.ownerDocument;if(!e.dispatchEvent(new T(`beforetoggle`,{cancelable:!0,oldState:`closed`,newState:`open`}))||!v(e,!1))return;let o=!1,n=e.popover,i=null,a=z(e,d.get(t)||new Set),l=z(e,c.get(t)||new Set);if(n===`auto`&&(R(c.get(t)||new Set,o,!0),g(a||t,o,!0),i=`auto`),n===`hint`&&(l?(g(l,o,!0),i=`hint`):(R(c.get(t)||new Set,o,!0),a?(g(a,o,!0),i=`auto`):i=`hint`)),n===`auto`||n===`hint`){if(n!==e.popover||!v(e,!1))return;L(t)||(o=!0),i===`auto`?(d.has(t)||d.set(t,new Set),d.get(t).add(e)):i===`hint`&&(c.has(t)||c.set(t,new Set),c.get(t).add(e))}k.delete(e);let p=t.activeElement;e.classList.add(`:popover-open`),y.set(e,`showing`),A.has(t)||A.set(t,new Set),A.get(t).add(e),G(M.get(e),!0),te(e),o&&p&&e.popover===`auto`&&k.set(e,p),U(e,`closed`,`open`)}function S(e,t=!1,o=!1){var n,i;if(!v(e,!0))return;let a=e.ownerDocument;if([`auto`,`hint`].includes(e.popover)&&(g(e,t,o),!v(e,!0)))return;let l=d.get(a)||new Set,p=l.has(e)&&m(l)===e;if(G(M.get(e),!1),M.delete(e),o&&(e.dispatchEvent(new T(`beforetoggle`,{oldState:`open`,newState:`closed`})),p&&m(l)!==e&&g(e,t,o),!v(e,!0)))return;(n=A.get(a))==null||n.delete(e),l.delete(e),(i=c.get(a))==null||i.delete(e),e.classList.remove(`:popover-open`),y.set(e,`hidden`),o&&U(e,`open`,`closed`);let u=k.get(e);u&&(k.delete(e),t&&u.focus())}function oe(e,t=!1,o=!1){let n=L(e);for(;n;)S(n,t,o),n=L(e)}function R(e,t=!1,o=!1){let n=j(e);for(;n;)S(n,t,o),n=j(e)}function V(e,t,o,n){let i=!1,a=!1;for(;i||!a;){a=!0;let l=null,p=!1;for(let u of t)if(u===e)p=!0;else if(p){l=u;break}if(!l)return;for(;P(l)===`showing`&&t.size;)S(m(t),o,n);t.has(e)&&m(t)!==e&&(i=!0),i&&(n=!1)}}function g(e,t,o){var n,i;let a=e.ownerDocument||e;if(e instanceof Document)return oe(a,t,o);if((n=c.get(a))!=null&&n.has(e)){V(e,c.get(a),t,o);return}R(c.get(a)||new Set,t,o),(i=d.get(a))!=null&&i.has(e)&&V(e,d.get(a),t,o)}var H=new WeakMap;function B(e){if(!e.isTrusted)return;let t=e.composedPath()[0];if(!t)return;let o=t.ownerDocument;if(!L(o))return;let i=J(t);if(i&&e.type===`pointerdown`)H.set(o,i);else if(e.type===`pointerup`){let a=H.get(o)===i;H.delete(o),a&&g(i||o,!1,!0)}}var D=new WeakMap;function G(e,t=!1){if(!e)return;D.has(e)||D.set(e,e.getAttribute(`aria-expanded`));let o=e.popoverTargetElement;if(o instanceof HTMLElement&&o.popover===`auto`)e.setAttribute(`aria-expanded`,String(t));else{let n=D.get(e);n?e.setAttribute(`aria-expanded`,n):e.removeAttribute(`aria-expanded`)}}var $=globalThis.ShadowRoot||function(){};var ne=`popover-polyfill`;function re(){return typeof HTMLElement<`u`&&typeof HTMLElement.prototype==`object`&&`popover`in HTMLElement.prototype}function h(e,t,o){let n=e[t];Object.defineProperty(e,t,{value(i){return n.call(this,o(i))}})}var ie=/(^|[^\\]):popover-open\b/g;function ae(){return typeof globalThis.CSSLayerBlockRule==`function`}function se(e){var t;let o=ae(),n=(e??((t=window.POPOVER_POLYFILL_OPTIONS)==null?void 0:t.layerName)??ne).split(`.`).map(CSS.escape).join(`.`);return`
${o?`@layer ${n} {`:``}
  :where([popover]) {
    position: fixed;
    z-index: 2147483647;
    inset: 0;
    padding: 0.25em;
    width: fit-content;
    height: fit-content;
    border-width: initial;
    border-color: initial;
    border-image: initial;
    border-style: solid;
    background-color: canvas;
    color: canvastext;
    overflow: auto;
    margin: auto;
  }

  :where([popover]:not(.\\:popover-open)) {
    display: none;
  }

  :where(dialog[popover].\\:popover-open) {
    display: block;
  }

  :where(dialog[popover][open]) {
    display: revert;
  }

  :where([anchor].\\:popover-open) {
    inset: auto;
  }

  :where([anchor]:popover-open) {
    inset: auto;
  }

  @supports not (background-color: canvas) {
    :where([popover]) {
      background-color: white;
      color: black;
    }
  }

  @supports (width: -moz-fit-content) {
    :where([popover]) {
      width: -moz-fit-content;
      height: -moz-fit-content;
    }
  }

  @supports not (inset: 0) {
    :where([popover]) {
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
    }
  }
${o?`}`:``}
`}var w=null;function O(e,t){let o=se(t);if(w===null)try{w=new CSSStyleSheet,w.replaceSync(o)}catch(n){w=!1}if(w===!1){let n=document.createElement(`style`);n.textContent=o,e instanceof Document?e.head.prepend(n):e.prepend(n)}else e.adoptedStyleSheets=[w,...e.adoptedStyleSheets]}function le(e){if(typeof window>`u`)return;let t=e?.layerName;window.ToggleEvent=window.ToggleEvent||T;function o(r){return r?.includes(`:popover-open`)&&(r=r.replace(ie,`$1.\\:popover-open`)),r}h(Document.prototype,`querySelector`,o),h(Document.prototype,`querySelectorAll`,o),h(Element.prototype,`querySelector`,o),h(Element.prototype,`querySelectorAll`,o),h(Element.prototype,`matches`,o),h(Element.prototype,`closest`,o),h(DocumentFragment.prototype,`querySelectorAll`,o),Object.defineProperties(HTMLElement.prototype,{popover:{enumerable:!0,configurable:!0,get(){if(!this.hasAttribute(`popover`))return null;let r=(this.getAttribute(`popover`)||``).toLowerCase();return r===``||r==`auto`?`auto`:r==`hint`?`hint`:`manual`},set(r){r===null?this.removeAttribute(`popover`):this.setAttribute(`popover`,r)}},showPopover:{enumerable:!0,configurable:!0,value(r={}){I(this)}},hidePopover:{enumerable:!0,configurable:!0,value(){S(this,!0,!0)}},togglePopover:{enumerable:!0,configurable:!0,value(r={}){return typeof r==`boolean`&&(r={force:r}),y.get(this)===`showing`&&r.force===void 0||r.force===!1?S(this,!0,!0):(r.force===void 0||r.force===!0)&&I(this),y.get(this)===`showing`}}});let n=Element.prototype.attachShadow;n&&Object.defineProperties(Element.prototype,{attachShadow:{enumerable:!0,configurable:!0,writable:!0,value(r){let s=n.call(this,r);return O(s,t),s}}});let i=HTMLElement.prototype.attachInternals;i&&Object.defineProperties(HTMLElement.prototype,{attachInternals:{enumerable:!0,configurable:!0,writable:!0,value(){let r=i.call(this);return r.shadowRoot&&O(r.shadowRoot,t),r}}});let a=new WeakMap;function l(r){Object.defineProperties(r.prototype,{popoverTargetElement:{enumerable:!0,configurable:!0,set(s){if(s===null)this.removeAttribute(`popovertarget`),a.delete(this);else if(s instanceof Element)this.setAttribute(`popovertarget`,``),a.set(this,s);else throw new TypeError(`popoverTargetElement must be an element or null`)},get(){if(this.localName!==`button`&&this.localName!==`input`||this.localName===`input`&&this.type!==`reset`&&this.type!==`image`&&this.type!==`button`||this.disabled||this.form&&this.type===`submit`)return null;let s=a.get(this);if(s&&s.isConnected)return s;if(s&&!s.isConnected)return a.delete(this),null;let f=b(this),E=this.getAttribute(`popovertarget`);return(f instanceof Document||f instanceof $)&&E&&f.getElementById(E)||null}},popoverTargetAction:{enumerable:!0,configurable:!0,get(){let s=(this.getAttribute(`popovertargetaction`)||``).toLowerCase();return s===`show`||s===`hide`?s:`toggle`},set(s){this.setAttribute(`popovertargetaction`,s)}}})}l(HTMLButtonElement),l(HTMLInputElement);let p=r=>{if(r.defaultPrevented)return;let s=r.composedPath(),f=s[0];if(!(f instanceof Element)||f?.shadowRoot)return;let E=b(f);if(!(E instanceof $||E instanceof Document))return;let F=s.find(C=>{var W;return(W=C.matches)==null?void 0:W.call(C,`[popovertargetaction],[popovertarget]`)});if(F){Q(F),r.preventDefault();return}},u=r=>{let s=r.key,f=r.target;!r.defaultPrevented&&f&&(s===`Escape`||s===`Esc`)&&g(f.ownerDocument,!0,!0)};(r=>{r.addEventListener(`click`,p),r.addEventListener(`keydown`,u),r.addEventListener(`pointerdown`,B),r.addEventListener(`pointerup`,B)})(document),O(document,t)}re()||le();