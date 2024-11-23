var c={POS_INT:0,NEG_INT:1,BYTE_STRING:2,UTF8_STRING:3,ARRAY:4,MAP:5,TAG:6,SIMPLE_FLOAT:7},A={DATE_STRING:0,DATE_EPOCH:1,POS_BIGINT:2,NEG_BIGINT:3,DECIMAL_FRAC:4,BIGFLOAT:5,BASE64URL_EXPECTED:21,BASE64_EXPECTED:22,BASE16_EXPECTED:23,CBOR:24,URI:32,BASE64URL:33,BASE64:34,MIME:36,SET:258,JSON:262,REGEXP:21066,SELF_DESCRIBED:55799,INVALID_16:65535,INVALID_32:4294967295,INVALID_64:0xffffffffffffffffn},h={ZERO:0,ONE:24,TWO:25,FOUR:26,EIGHT:27,INDEFINITE:31},w={FALSE:20,TRUE:21,NULL:22,UNDEFINED:23},u=class{static BREAK=Symbol.for("github.com/hildjj/cbor2/break");static ENCODED=Symbol.for("github.com/hildjj/cbor2/cbor-encoded");static LENGTH=Symbol.for("github.com/hildjj/cbor2/length")},p={MIN:-(2n**63n),MAX:2n**64n-1n};var O=(r=>(r[r.NEVER=-1]="NEVER",r[r.PREFERRED=0]="PREFERRED",r[r.ALWAYS=1]="ALWAYS",r))(O||{});function S(r,e){let[t,n,i]=r,[o,s,a]=e,f=Math.min(i.length,a.length);for(let l=0;l<f;l++){let d=i[l]-a[l];if(d!==0)return d}return 0}var E=class r{static#e=new Map;tag;contents;constructor(e,t=void 0){this.tag=e,this.contents=t}get noChildren(){return!!r.#e.get(this.tag)?.noChildren}static registerDecoder(e,t,n){let i=this.#e.get(e);return this.#e.set(e,t),i&&("comment"in t||(t.comment=i.comment),"noChildren"in t||(t.noChildren=i.noChildren)),n&&!t.comment&&(t.comment=()=>`(${n})`),i}static clearDecoder(e){let t=this.#e.get(e);return this.#e.delete(e),t}*[Symbol.iterator](){yield this.contents}push(e){return this.contents=e,1}decode(e){let t=r.#e.get(this.tag);return t?t(this,e):this}comment(e,t){let n=r.#e.get(this.tag);if(n?.comment)return n.comment(this,e,t)}toCBOR(){return[this.tag,this.contents]}[Symbol.for("nodejs.util.inspect.custom")](e,t,n){return`${this.tag}(${n(this.contents,t)})`}};function j(r){if(r!=null&&typeof r=="object")return r[u.ENCODED]}function M(r){if(r!=null&&typeof r=="object")return r[u.LENGTH]}function D(r,e){Object.defineProperty(r,u.ENCODED,{configurable:!0,enumerable:!1,value:e})}function b(r,e){let t=Object(r);return D(t,e),t}function U(r){let e=Math.ceil(r.length/2),t=new Uint8Array(e);e--;for(let n=r.length,i=n-2;n>=0;n=i,i-=2,e--)t[e]=parseInt(r.substring(i,n),16);return t}function L(r){return r.reduce((e,t)=>e+t.toString(16).padStart(2,"0"),"")}function k(r){let e=r.reduce((i,o)=>i+o.length,0),t=new Uint8Array(e),n=0;for(let i of r)t.set(i,n),n+=i.length;return t}function _(r){let e=atob(r);return Uint8Array.from(e,t=>t.codePointAt(0))}function B(r){let e="";for(let t of r){let n=t.codePointAt(0)?.toString(16).padStart(4,"0");e&&(e+=", "),e+=`U+${n}`}return e}var T=class r{static defaultOptions={chunkSize:4096};#e;#t=[];#r=null;#n=0;#i=0;constructor(e={}){if(this.#e={...r.defaultOptions,...e},this.#e.chunkSize<8)throw new RangeError(`Expected size >= 8, got ${this.#e.chunkSize}`);this.#o()}get length(){return this.#i}read(){this.#c();let e=new Uint8Array(this.#i),t=0;for(let n of this.#t)e.set(n,t),t+=n.length;return this.#o(),e}write(e){let t=e.length;t>this.#l()?(this.#c(),t>this.#e.chunkSize?(this.#t.push(e),this.#o()):(this.#o(),this.#t[this.#t.length-1].set(e),this.#n=t)):(this.#t[this.#t.length-1].set(e,this.#n),this.#n+=t),this.#i+=t}writeUint8(e){this.#s(1),this.#r.setUint8(this.#n,e),this.#a(1)}writeUint16(e,t=!1){this.#s(2),this.#r.setUint16(this.#n,e,t),this.#a(2)}writeUint32(e,t=!1){this.#s(4),this.#r.setUint32(this.#n,e,t),this.#a(4)}writeBigUint64(e,t=!1){this.#s(8),this.#r.setBigUint64(this.#n,e,t),this.#a(8)}writeInt16(e,t=!1){this.#s(2),this.#r.setInt16(this.#n,e,t),this.#a(2)}writeInt32(e,t=!1){this.#s(4),this.#r.setInt32(this.#n,e,t),this.#a(4)}writeBigInt64(e,t=!1){this.#s(8),this.#r.setBigInt64(this.#n,e,t),this.#a(8)}writeFloat32(e,t=!1){this.#s(4),this.#r.setFloat32(this.#n,e,t),this.#a(4)}writeFloat64(e,t=!1){this.#s(8),this.#r.setFloat64(this.#n,e,t),this.#a(8)}clear(){this.#i=0,this.#t=[],this.#o()}#o(){let e=new Uint8Array(this.#e.chunkSize);this.#t.push(e),this.#n=0,this.#r=new DataView(e.buffer,e.byteOffset,e.byteLength)}#c(){if(this.#n===0){this.#t.pop();return}let e=this.#t.length-1;this.#t[e]=this.#t[e].subarray(0,this.#n),this.#n=0,this.#r=null}#l(){let e=this.#t.length-1;return this.#t[e].length-this.#n}#s(e){this.#l()<e&&(this.#c(),this.#o())}#a(e){this.#n+=e,this.#i+=e}};function v(r,e=0,t=!1){let n=r[e]&128?-1:1,i=(r[e]&124)>>2,o=(r[e]&3)<<8|r[e+1];if(i===0){if(t&&o!==0)throw new Error(`Unwanted subnormal: ${n*5960464477539063e-23*o}`);return n*5960464477539063e-23*o}else if(i===31)return o?NaN:n*(1/0);return n*2**(i-25)*(1024+o)}function G(r){let e=new DataView(new ArrayBuffer(4));e.setFloat32(0,r,!1);let t=e.getUint32(0,!1);if(t&8191)return null;let n=t>>16&32768,i=t>>23&255,o=t&8388607;if(!(i===0&&o===0))if(i>=113&&i<=142)n+=(i-112<<10)+(o>>13);else if(i>=103&&i<113){if(o&(1<<126-i)-1)return null;n+=o+8388608>>126-i}else if(i===255)n|=31744,n|=o>>13;else return null;return n}function $(r){if(r!==0){let e=new ArrayBuffer(8),t=new DataView(e);t.setFloat64(0,r,!1);let n=t.getBigUint64(0,!1);if((n&0x7ff0000000000000n)===0n)return n&0x8000000000000000n?-0:0}return r}function K(r){switch(r.length){case 2:v(r,0,!0);break;case 4:{let e=new DataView(r.buffer,r.byteOffset,r.byteLength),t=e.getUint32(0,!1);if(!(t&2139095040)&&t&8388607)throw new Error(`Unwanted subnormal: ${e.getFloat32(0,!1)}`);break}case 8:{let e=new DataView(r.buffer,r.byteOffset,r.byteLength),t=e.getBigUint64(0,!1);if((t&0x7ff0000000000000n)===0n&&t&0x000fffffffffffn)throw new Error(`Unwanted subnormal: ${e.getFloat64(0,!1)}`);break}default:throw new TypeError(`Bad input to isSubnormal: ${r}`)}}var{ENCODED:ve}=u,H=c.SIMPLE_FLOAT<<5|h.TWO,Z=c.SIMPLE_FLOAT<<5|h.FOUR,q=c.SIMPLE_FLOAT<<5|h.EIGHT,J=c.SIMPLE_FLOAT<<5|w.TRUE,X=c.SIMPLE_FLOAT<<5|w.FALSE,Q=c.SIMPLE_FLOAT<<5|w.UNDEFINED,ee=c.SIMPLE_FLOAT<<5|w.NULL,te=new TextEncoder,re={...T.defaultOptions,avoidInts:!1,cde:!1,collapseBigInts:!0,dcbor:!1,float64:!1,flushToZero:!1,forceEndian:null,ignoreOriginalEncoding:!1,largeNegativeAsBigInt:!1,reduceUnsafeNumbers:!1,rejectBigInts:!1,rejectCustomSimples:!1,rejectDuplicateKeys:!1,rejectFloats:!1,rejectUndefined:!1,simplifyNegativeZero:!1,sortKeys:null,stringNormalization:null},z={cde:!0,ignoreOriginalEncoding:!0,sortKeys:S},ne={...z,dcbor:!0,largeNegativeAsBigInt:!0,reduceUnsafeNumbers:!0,rejectCustomSimples:!0,rejectDuplicateKeys:!0,rejectUndefined:!0,simplifyNegativeZero:!0,stringNormalization:"NFC"};function Y(r){let e=r<0;return typeof r=="bigint"?[e?-r-1n:r,e]:[e?-r-1:r,e]}function R(r,e,t){if(t.rejectFloats)throw new Error(`Attempt to encode an unwanted floating point number: ${r}`);if(isNaN(r))e.writeUint8(H),e.writeUint16(32256);else if(!t.float64&&Math.fround(r)===r){let n=G(r);n===null?(e.writeUint8(Z),e.writeFloat32(r)):(e.writeUint8(H),e.writeUint16(n))}else e.writeUint8(q),e.writeFloat64(r)}function g(r,e,t){let[n,i]=Y(r);if(i&&t)throw new TypeError(`Negative size: ${r}`);t??=i?c.NEG_INT:c.POS_INT,t<<=5,n<24?e.writeUint8(t|n):n<=255?(e.writeUint8(t|h.ONE),e.writeUint8(n)):n<=65535?(e.writeUint8(t|h.TWO),e.writeUint16(n)):n<=4294967295?(e.writeUint8(t|h.FOUR),e.writeUint32(n)):(e.writeUint8(t|h.EIGHT),e.writeBigUint64(BigInt(n)))}function F(r,e,t){typeof r=="number"?g(r,e,c.TAG):typeof r=="object"&&!t.ignoreOriginalEncoding&&u.ENCODED in r?e.write(r[u.ENCODED]):r<=Number.MAX_SAFE_INTEGER?g(Number(r),e,c.TAG):(e.writeUint8(c.TAG<<5|h.EIGHT),e.writeBigUint64(BigInt(r)))}function V(r,e,t){let[n,i]=Y(r);if(t.collapseBigInts&&(!t.largeNegativeAsBigInt||r>=-0x8000000000000000n)){if(n<=0xffffffffn){g(Number(r),e);return}if(n<=0xffffffffffffffffn){let l=(i?c.NEG_INT:c.POS_INT)<<5;e.writeUint8(l|h.EIGHT),e.writeBigUint64(n);return}}if(t.rejectBigInts)throw new Error(`Attempt to encode unwanted bigint: ${r}`);let o=i?A.NEG_BIGINT:A.POS_BIGINT,s=n.toString(16),a=s.length%2?"0":"";F(o,e,t);let f=U(a+s);g(f.length,e,c.BYTE_STRING),e.write(f)}function ie(r,e,t){t.flushToZero&&(r=$(r)),Object.is(r,-0)?t.simplifyNegativeZero?t.avoidInts?R(0,e,t):g(0,e):R(r,e,t):!t.avoidInts&&Number.isSafeInteger(r)?g(r,e):t.reduceUnsafeNumbers&&Math.floor(r)===r&&r>=p.MIN&&r<=p.MAX?V(BigInt(r),e,t):R(r,e,t)}function oe(r,e,t){let n=t.stringNormalization?r.normalize(t.stringNormalization):r,i=te.encode(n);g(i.length,e,c.UTF8_STRING),e.write(i)}function se(r,e,t){let n=r;W(n,n.length,c.ARRAY,e,t);for(let i of n)y(i,e,t)}function ae(r,e){let t=r;g(t.length,e,c.BYTE_STRING),e.write(t)}var ce=new Map([[Array,se],[Uint8Array,ae]]);function W(r,e,t,n,i){let o=M(r);o&&!i.ignoreOriginalEncoding?n.write(o):g(e,n,t)}function le(r,e,t){if(r===null){e.writeUint8(ee);return}if(!t.ignoreOriginalEncoding&&u.ENCODED in r){e.write(r[u.ENCODED]);return}let n=ce.get(r.constructor);if(n){let o=n(r,e,t);o&&((typeof o[0]=="bigint"||isFinite(Number(o[0])))&&F(o[0],e,t),y(o[1],e,t));return}if(typeof r.toCBOR=="function"){let o=r.toCBOR(e,t);o&&((typeof o[0]=="bigint"||isFinite(Number(o[0])))&&F(o[0],e,t),y(o[1],e,t));return}if(typeof r.toJSON=="function"){y(r.toJSON(),e,t);return}let i=Object.entries(r).map(o=>[o[0],o[1],x(o[0],t)]);t.sortKeys&&i.sort(t.sortKeys),W(r,i.length,c.MAP,e,t);for(let[o,s,a]of i)e.write(a),y(s,e,t)}function y(r,e,t){switch(typeof r){case"number":ie(r,e,t);break;case"bigint":V(r,e,t);break;case"string":oe(r,e,t);break;case"boolean":e.writeUint8(r?J:X);break;case"undefined":if(t.rejectUndefined)throw new Error("Attempt to encode unwanted undefined.");e.writeUint8(Q);break;case"object":le(r,e,t);break;case"symbol":throw new TypeError(`Unknown symbol: ${r.toString()}`);default:throw new TypeError(`Unknown type: ${typeof r}, ${String(r)}`)}}function x(r,e={}){let t={...re};e.dcbor?Object.assign(t,ne):e.cde&&Object.assign(t,z),Object.assign(t,e);let n=new T(t);return y(r,n,t),n.read()}var N=class r{static KnownSimple=new Map([[w.FALSE,!1],[w.TRUE,!0],[w.NULL,null],[w.UNDEFINED,void 0]]);value;constructor(e){this.value=e}static create(e){return r.KnownSimple.has(e)?r.KnownSimple.get(e):new r(e)}toCBOR(e,t){if(t.rejectCustomSimples)throw new Error(`Cannot encode non-standard Simple value: ${this.value}`);g(this.value,e,c.SIMPLE_FLOAT)}toString(){return`simple(${this.value})`}decode(){return r.KnownSimple.has(this.value)?r.KnownSimple.get(this.value):this}[Symbol.for("nodejs.util.inspect.custom")](e,t,n){return`simple(${n(this.value,t)})`}};var fe=new TextDecoder("utf8",{fatal:!0,ignoreBOM:!0}),I=class r{static defaultOptions={maxDepth:1024,encoding:"hex",requirePreferred:!1};#e;#t;#r=0;#n;constructor(e,t){if(this.#n={...r.defaultOptions,...t},typeof e=="string")switch(this.#n.encoding){case"hex":this.#e=U(e);break;case"base64":this.#e=_(e);break;default:throw new TypeError(`Encoding not implemented: "${this.#n.encoding}"`)}else this.#e=e;this.#t=new DataView(this.#e.buffer,this.#e.byteOffset,this.#e.byteLength)}toHere(e){return this.#e.subarray(e,this.#r)}*[Symbol.iterator](){if(yield*this.#i(0),this.#r!==this.#e.length)throw new Error("Extra data in input")}*#i(e){if(e++>this.#n.maxDepth)throw new Error(`Maximum depth ${this.#n.maxDepth} exceeded`);let t=this.#r,n=this.#t.getUint8(this.#r++),i=n>>5,o=n&31,s=o,a=!1,f=0;switch(o){case h.ONE:if(f=1,s=this.#t.getUint8(this.#r),i===c.SIMPLE_FLOAT){if(s<32)throw new Error(`Invalid simple encoding in extra byte: ${s}`);a=!0}else if(this.#n.requirePreferred&&s<24)throw new Error(`Unexpectedly long integer encoding (1) for ${s}`);break;case h.TWO:if(f=2,i===c.SIMPLE_FLOAT)s=v(this.#e,this.#r);else if(s=this.#t.getUint16(this.#r,!1),this.#n.requirePreferred&&s<=255)throw new Error(`Unexpectedly long integer encoding (2) for ${s}`);break;case h.FOUR:if(f=4,i===c.SIMPLE_FLOAT)s=this.#t.getFloat32(this.#r,!1);else if(s=this.#t.getUint32(this.#r,!1),this.#n.requirePreferred&&s<=65535)throw new Error(`Unexpectedly long integer encoding (4) for ${s}`);break;case h.EIGHT:{if(f=8,i===c.SIMPLE_FLOAT)s=this.#t.getFloat64(this.#r,!1);else if(s=this.#t.getBigUint64(this.#r,!1),s<=Number.MAX_SAFE_INTEGER&&(s=Number(s)),this.#n.requirePreferred&&s<=4294967295)throw new Error(`Unexpectedly long integer encoding (8) for ${s}`);break}case 28:case 29:case 30:throw new Error(`Additional info not implemented: ${o}`);case h.INDEFINITE:switch(i){case c.POS_INT:case c.NEG_INT:case c.TAG:throw new Error(`Invalid indefinite encoding for MT ${i}`);case c.SIMPLE_FLOAT:yield[i,o,u.BREAK,t,0];return}s=1/0;break;default:a=!0}switch(this.#r+=f,i){case c.POS_INT:yield[i,o,s,t,f];break;case c.NEG_INT:yield[i,o,typeof s=="bigint"?-1n-s:-1-Number(s),t,f];break;case c.BYTE_STRING:s===1/0?yield*this.#c(i,e,t):yield[i,o,this.#o(s),t,s];break;case c.UTF8_STRING:s===1/0?yield*this.#c(i,e,t):yield[i,o,fe.decode(this.#o(s)),t,s];break;case c.ARRAY:if(s===1/0)yield*this.#c(i,e,t,!1);else{let l=Number(s);yield[i,o,l,t,f];for(let d=0;d<l;d++)yield*this.#i(e+1)}break;case c.MAP:if(s===1/0)yield*this.#c(i,e,t,!1);else{let l=Number(s);yield[i,o,l,t,f];for(let d=0;d<l;d++)yield*this.#i(e),yield*this.#i(e)}break;case c.TAG:yield[i,o,s,t,f],yield*this.#i(e);break;case c.SIMPLE_FLOAT:{let l=s;a&&(s=N.create(Number(s))),yield[i,o,s,t,l];break}}}#o(e){let t=this.#e.subarray(this.#r,this.#r+=e);if(t.length!==e)throw new Error(`Unexpected end of stream reading ${e} bytes, got ${t.length}`);return t}*#c(e,t,n,i=!0){for(yield[e,h.INDEFINITE,1/0,n,1/0];;){let o=this.#i(t),s=o.next(),[a,f,l]=s.value;if(l===u.BREAK){yield s.value,o.next();return}if(i){if(a!==e)throw new Error(`Unmatched major type.  Expected ${e}, got ${a}.`);if(f===h.INDEFINITE)throw new Error("New stream started in typed stream")}yield s.value,yield*o}}};var he=new Map([[h.ZERO,1],[h.ONE,2],[h.TWO,3],[h.FOUR,5],[h.EIGHT,9]]),ue=new Uint8Array(0),m=class r{static defaultDecodeOptions={...I.defaultOptions,ParentType:r,boxed:!1,cde:!1,dcbor:!1,diagnosticSizes:O.PREFERRED,convertUnsafeIntsToFloat:!1,pretty:!1,preferMap:!1,rejectLargeNegatives:!1,rejectBigInts:!1,rejectDuplicateKeys:!1,rejectFloats:!1,rejectInts:!1,rejectLongLoundNaN:!1,rejectLongFloats:!1,rejectNegativeZero:!1,rejectSimple:!1,rejectStreaming:!1,rejectStringsNotNormalizedAs:null,rejectSubnormals:!1,rejectUndefined:!1,rejectUnsafeFloatInts:!1,saveOriginal:!1,sortKeys:null};static cdeDecodeOptions={cde:!0,rejectStreaming:!0,requirePreferred:!0,sortKeys:S};static dcborDecodeOptions={...this.cdeDecodeOptions,dcbor:!0,convertUnsafeIntsToFloat:!0,rejectDuplicateKeys:!0,rejectLargeNegatives:!0,rejectLongLoundNaN:!0,rejectLongFloats:!0,rejectNegativeZero:!0,rejectSimple:!0,rejectUndefined:!0,rejectUnsafeFloatInts:!0,rejectStringsNotNormalizedAs:"NFC"};parent;mt;ai;left;offset;count=0;children=[];depth=0;#e;#t=null;constructor(e,t,n,i){if([this.mt,this.ai,,this.offset]=e,this.left=t,this.parent=n,this.#e=i,n&&(this.depth=n.depth+1),this.mt===c.MAP&&(this.#e.sortKeys||this.#e.rejectDuplicateKeys)&&(this.#t=[]),this.#e.rejectStreaming&&this.ai===h.INDEFINITE)throw new Error("Streaming not supported")}get isStreaming(){return this.left===1/0}get done(){return this.left===0}static create(e,t,n,i){let[o,s,a,f]=e;switch(o){case c.POS_INT:case c.NEG_INT:{if(n.rejectInts)throw new Error(`Unexpected integer: ${a}`);if(n.rejectLargeNegatives&&a<-0x8000000000000000n)throw new Error(`Invalid 65bit negative number: ${a}`);let l=a;return n.convertUnsafeIntsToFloat&&l>=p.MIN&&l<=p.MAX&&(l=Number(a)),n.boxed?b(l,i.toHere(f)):l}case c.SIMPLE_FLOAT:if(s>h.ONE){if(n.rejectFloats)throw new Error(`Decoding unwanted floating point number: ${a}`);if(n.rejectNegativeZero&&Object.is(a,-0))throw new Error("Decoding negative zero");if(n.rejectLongLoundNaN&&isNaN(a)){let l=i.toHere(f);if(l.length!==3||l[1]!==126||l[2]!==0)throw new Error(`Invalid NaN encoding: "${L(l)}"`)}if(n.rejectSubnormals&&K(i.toHere(f+1)),n.rejectLongFloats){let l=x(a,{chunkSize:9,reduceUnsafeNumbers:n.rejectUnsafeFloatInts});if(l[0]>>5!==o)throw new Error(`Should have been encoded as int, not float: ${a}`);if(l.length<he.get(s))throw new Error(`Number should have been encoded shorter: ${a}`)}if(typeof a=="number"&&n.boxed)return b(a,i.toHere(f))}else{if(n.rejectSimple&&a instanceof N)throw new Error(`Invalid simple value: ${a}`);if(n.rejectUndefined&&a===void 0)throw new Error("Unexpected undefined")}return a;case c.BYTE_STRING:case c.UTF8_STRING:if(a===1/0)return new n.ParentType(e,1/0,t,n);if(n.rejectStringsNotNormalizedAs&&typeof a=="string"){let l=a.normalize(n.rejectStringsNotNormalizedAs);if(a!==l)throw new Error(`String not normalized as "${n.rejectStringsNotNormalizedAs}", got [${B(a)}] instead of [${B(l)}]`)}return n.boxed?b(a,i.toHere(f)):a;case c.ARRAY:return new n.ParentType(e,a,t,n);case c.MAP:return new n.ParentType(e,a*2,t,n);case c.TAG:{let l=new n.ParentType(e,1,t,n);return l.children=new E(a),l}}throw new TypeError(`Invalid major type: ${o}`)}push(e,t,n){if(this.children.push(e),this.#t){let i=j(e)||t.toHere(n);this.#t.push(i)}return--this.left}replaceLast(e,t,n){let i,o=-1/0;if(this.children instanceof E?(o=0,i=this.children.contents,this.children.contents=e):(o=this.children.length-1,i=this.children[o],this.children[o]=e),this.#t){let s=j(e)||n.toHere(t.offset);this.#t[o]=s}return i}convert(e){let t;switch(this.mt){case c.ARRAY:t=this.children;break;case c.MAP:{let n=this.#r();if(this.#e.sortKeys){let i;for(let o of n){if(i&&this.#e.sortKeys(i,o)>=0)throw new Error(`Duplicate or out of order key: "0x${o[2]}"`);i=o}}else if(this.#e.rejectDuplicateKeys){let i=new Set;for(let[o,s,a]of n){let f=L(a);if(i.has(f))throw new Error(`Duplicate key: "0x${f}"`);i.add(f)}}t=!this.#e.boxed&&!this.#e.preferMap&&n.every(([i])=>typeof i=="string")?Object.fromEntries(n):new Map(n);break}case c.BYTE_STRING:return k(this.children);case c.UTF8_STRING:{let n=this.children.join("");t=this.#e.boxed?b(n,e.toHere(this.offset)):n;break}case c.TAG:t=this.children.decode(this.#e);break;default:throw new TypeError(`Invalid mt on convert: ${this.mt}`)}return this.#e.saveOriginal&&t&&typeof t=="object"&&D(t,e.toHere(this.offset)),t}#r(){let e=this.children,t=e.length;if(t%2)throw new Error("Missing map value");let n=new Array(t/2);if(this.#t)for(let i=0;i<t;i+=2)n[i>>1]=[e[i],e[i+1],this.#t[i]];else for(let i=0;i<t;i+=2)n[i>>1]=[e[i],e[i+1],ue];return n}};function C(r,e={}){let t={...m.defaultDecodeOptions};if(e.dcbor?Object.assign(t,m.dcborDecodeOptions):e.cde&&Object.assign(t,m.cdeDecodeOptions),Object.assign(t,e),Object.hasOwn(t,"rejectLongNumbers"))throw new TypeError("rejectLongNumbers has changed to requirePreferred");t.boxed&&(t.saveOriginal=!0);let n=new I(r,t),i,o;for(let s of n){if(o=m.create(s,i,t,n),s[2]===u.BREAK)if(i?.isStreaming)i.left=0;else throw new Error("Unexpected BREAK");else i&&i.push(o,n,s[3]);for(o instanceof m&&(i=o);i?.done;){o=i.convert(n);let a=i.parent;a?.replaceLast(o,i,n),i=a}}return o}function de(){let r=new Uint8Array(32);return window.crypto.getRandomValues(r),r}async function ge(r,e){console.log("storeCredential 1");let t=await crypto.subtle.importKey("jwk",e,{name:"ECDSA",namedCurve:"P-256"},!0,[]);console.log("storeCredential 2");let i=(await chrome.storage.sync.get("credentials")||{}).credentials||[];i.push({credentialId:Array.from(new Uint8Array(r)),publicKey:await crypto.subtle.exportKey("jwk",t)}),await chrome.storage.sync.set({credentials:i})}async function ot(){return(await chrome.storage.sync.get("credentials")||{}).credentials||[]}async function st(){let e={challenge:de(),rp:{name:"Example Extension"},user:{id:new Uint8Array(16),name:"user@example.com",displayName:"Example User"},pubKeyCredParams:[{alg:-7,type:"public-key"},{alg:-257,type:"public-key"}],authenticatorSelection:{requireResidentKey:!0},timeout:6e4,extensions:{"hmac-secret":!0},attestation:"direct"};try{let t=await navigator.credentials.create({publicKey:e});if(t){console.log("Credential created:",t),console.log("Client Extension Results:",t.getClientExtensionResults());let{rawId:n,response:i}=t,o=i.attestationObject,s=C(new Uint8Array(o));console.log("Decoded attestation object:",s);let a=s.authData;console.log("registerCredential 5");let f=a instanceof ArrayBuffer?a:a.buffer;console.log("registerCredential 5.5");let l=we(f);console.log("registerCredential 6"),await ge(n,l),console.log("registerCredential 7")}}catch(t){console.error("Error creating credential:",t)}}function we(r){console.log("extractPublicKeyFromAuthData authData: ",r);let e=new DataView(r),t=0,n=r.slice(t,t+32);t+=32;let i=e.getUint8(t);t+=1;let o=e.getUint32(t,!1);t+=4,console.log("RP ID Hash:",n),console.log("Flags:",i),console.log("Sign Count:",o);let s=r.slice(t,t+16);t+=16,console.log("AAGUID:",s);let a=e.getUint16(t,!1);t+=2,console.log("Credential ID Length:",a);let f=r.slice(t,t+a);t+=a,console.log("Credential ID:",f),console.log("remaining offset, byteLength: ",t,r.byteLength);let l=new Uint8Array(r.slice(t));console.log("Public Key Bytes:",l);let d=C(l);console.log("Public Key (CBOR Decoded):",d);let P=d;return console.log("jwk:",P),P}export{ot as getStoredCredentials,st as registerCredential,ge as storeCredential};