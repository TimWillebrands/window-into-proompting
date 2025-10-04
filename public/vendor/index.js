// node_modules/streaming-markdown/smd.js
var DOCUMENT = 1;
var PARAGRAPH = 2;
var HEADING_1 = 3;
var HEADING_2 = 4;
var HEADING_3 = 5;
var HEADING_4 = 6;
var HEADING_5 = 7;
var HEADING_6 = 8;
var CODE_BLOCK = 9;
var CODE_FENCE = 10;
var CODE_INLINE = 11;
var ITALIC_AST = 12;
var ITALIC_UND = 13;
var STRONG_AST = 14;
var STRONG_UND = 15;
var STRIKE = 16;
var LINK = 17;
var RAW_URL = 18;
var IMAGE = 19;
var BLOCKQUOTE = 20;
var LINE_BREAK = 21;
var RULE = 22;
var LIST_UNORDERED = 23;
var LIST_ORDERED = 24;
var LIST_ITEM = 25;
var CHECKBOX = 26;
var TABLE = 27;
var TABLE_ROW = 28;
var TABLE_CELL = 29;
var EQUATION_BLOCK = 30;
var EQUATION_INLINE = 31;
var NEWLINE = 101;
var MAYBE_URL = 102;
var MAYBE_TASK = 103;
var MAYBE_BR = 104;
var MAYBE_EQ_BLOCK = 105;
var HREF = 1;
var SRC = 2;
var LANG = 4;
var CHECKED = 8;
var START = 16;
function attr_to_html_attr(type) {
  switch (type) {
    case HREF:
      return "href";
    case SRC:
      return "src";
    case LANG:
      return "class";
    case CHECKED:
      return "checked";
    case START:
      return "start";
  }
}
var level_to_heading = (level) => {
  switch (level) {
    case 1:
      return HEADING_1;
    case 2:
      return HEADING_2;
    case 3:
      return HEADING_3;
    case 4:
      return HEADING_4;
    case 5:
      return HEADING_5;
    default:
      return HEADING_6;
  }
};
var heading_from_level = level_to_heading;
var TOKEN_ARRAY_CAP = 24;
function parser(renderer) {
  const tokens = new Uint32Array(TOKEN_ARRAY_CAP);
  tokens[0] = DOCUMENT;
  return {
    renderer,
    text: "",
    pending: "",
    tokens,
    len: 0,
    token: DOCUMENT,
    fence_end: 0,
    blockquote_idx: 0,
    hr_char: "",
    hr_chars: 0,
    fence_start: 0,
    spaces: new Uint8Array(TOKEN_ARRAY_CAP),
    indent: "",
    indent_len: 0,
    table_state: 0
  };
}
function parser_end(p) {
  if (p.pending.length > 0) {
    parser_write(p, `
`);
  }
}
function add_text(p) {
  if (p.text.length === 0)
    return;
  console.assert(p.len > 0, "Never adding text to root");
  p.renderer.add_text(p.renderer.data, p.text);
  p.text = "";
}
function end_token(p) {
  console.assert(p.len > 0, "No nodes to end");
  p.len -= 1;
  p.token = p.tokens[p.len];
  p.renderer.end_token(p.renderer.data);
}
function add_token(p, token) {
  if ((p.tokens[p.len] === LIST_ORDERED || p.tokens[p.len] === LIST_UNORDERED) && token !== LIST_ITEM) {
    end_token(p);
  }
  p.len += 1;
  p.tokens[p.len] = token;
  p.token = token;
  p.renderer.add_token(p.renderer.data, token);
}
function idx_of_token(p, token, start_idx) {
  while (start_idx <= p.len) {
    if (p.tokens[start_idx] === token) {
      return start_idx;
    }
    start_idx += 1;
  }
  return -1;
}
function end_tokens_to_len(p, len) {
  p.fence_start = 0;
  while (p.len > len) {
    end_token(p);
  }
}
function end_tokens_to_indent(p, indent) {
  let idx = 0;
  for (let i = 0;i <= p.len; i += 1) {
    indent -= p.spaces[i];
    if (indent < 0) {
      break;
    }
    switch (p.tokens[i]) {
      case CODE_BLOCK:
      case CODE_FENCE:
      case BLOCKQUOTE:
      case LIST_ITEM:
        idx = i;
        break;
    }
  }
  while (p.len > idx) {
    end_token(p);
  }
  return indent;
}
function continue_or_add_list(p, list_token) {
  let list_idx = -1;
  let item_idx = -1;
  for (let i = p.blockquote_idx + 1;i <= p.len; i += 1) {
    if (p.tokens[i] === LIST_ITEM) {
      if (p.indent_len < p.spaces[i]) {
        item_idx = -1;
        break;
      }
      item_idx = i;
    } else if (p.tokens[i] === list_token) {
      list_idx = i;
    }
  }
  if (item_idx === -1) {
    if (list_idx === -1) {
      end_tokens_to_len(p, p.blockquote_idx);
      add_token(p, list_token);
      return true;
    }
    end_tokens_to_len(p, list_idx);
    return false;
  }
  end_tokens_to_len(p, item_idx);
  add_token(p, list_token);
  return true;
}
function add_list_item(p, prefix_length) {
  add_token(p, LIST_ITEM);
  p.spaces[p.len] = p.indent_len + prefix_length;
  clear_root_pending(p);
  p.token = MAYBE_TASK;
}
function clear_root_pending(p) {
  p.indent = "";
  p.indent_len = 0;
  p.pending = "";
}
function is_digit(charcode) {
  switch (charcode) {
    case 48:
    case 49:
    case 50:
    case 51:
    case 52:
    case 53:
    case 54:
    case 55:
    case 56:
    case 57:
      return true;
    default:
      return false;
  }
}
function is_delimeter(charcode) {
  switch (charcode) {
    case 32:
    case 58:
    case 59:
    case 41:
    case 44:
    case 33:
    case 46:
    case 63:
    case 93:
    case 10:
      return true;
    default:
      return false;
  }
}
function is_delimeter_or_number(charcode) {
  return is_digit(charcode) || is_delimeter(charcode);
}
function parser_write(p, chunk) {
  for (const char of chunk) {
    if (p.token === NEWLINE) {
      switch (char) {
        case " ":
          p.indent_len += 1;
          continue;
        case "\t":
          p.indent_len += 4;
          continue;
      }
      let indent = end_tokens_to_indent(p, p.indent_len);
      p.indent_len = 0;
      p.token = p.tokens[p.len];
      if (indent > 0) {
        parser_write(p, " ".repeat(indent));
      }
    }
    const pending_with_char = p.pending + char;
    switch (p.token) {
      case LINE_BREAK:
      case DOCUMENT:
      case BLOCKQUOTE:
      case LIST_ORDERED:
      case LIST_UNORDERED:
        console.assert(p.text.length === 0, "Root should not have any text");
        switch (p.pending[0]) {
          case undefined:
            p.pending = char;
            continue;
          case " ":
            console.assert(p.pending.length === 1);
            p.pending = char;
            p.indent += " ";
            p.indent_len += 1;
            continue;
          case "\t":
            console.assert(p.pending.length === 1);
            p.pending = char;
            p.indent += "\t";
            p.indent_len += 4;
            continue;
          case `
`:
            console.assert(p.pending.length === 1);
            if (p.tokens[p.len] === LIST_ITEM && p.token === LINE_BREAK) {
              end_token(p);
              clear_root_pending(p);
              p.pending = char;
              continue;
            }
            end_tokens_to_len(p, p.blockquote_idx);
            clear_root_pending(p);
            p.blockquote_idx = 0;
            p.fence_start = 0;
            p.pending = char;
            continue;
          case "#":
            switch (char) {
              case "#":
                if (p.pending.length < 6) {
                  p.pending = pending_with_char;
                  continue;
                }
                break;
              case " ":
                end_tokens_to_indent(p, p.indent_len);
                add_token(p, heading_from_level(p.pending.length));
                clear_root_pending(p);
                continue;
            }
            break;
          case ">": {
            const next_blockquote_idx = idx_of_token(p, BLOCKQUOTE, p.blockquote_idx + 1);
            if (next_blockquote_idx === -1) {
              end_tokens_to_len(p, p.blockquote_idx);
              p.blockquote_idx += 1;
              p.fence_start = 0;
              add_token(p, BLOCKQUOTE);
            } else {
              p.blockquote_idx = next_blockquote_idx;
            }
            clear_root_pending(p);
            p.pending = char;
            continue;
          }
          case "-":
          case "*":
          case "_":
            if (p.hr_chars === 0) {
              console.assert(p.pending.length === 1, "Pending should be one character");
              p.hr_chars = 1;
              p.hr_char = p.pending;
            }
            if (p.hr_chars > 0) {
              switch (char) {
                case p.hr_char:
                  p.hr_chars += 1;
                  p.pending = pending_with_char;
                  continue;
                case " ":
                  p.pending = pending_with_char;
                  continue;
                case `
`:
                  if (p.hr_chars < 3)
                    break;
                  end_tokens_to_indent(p, p.indent_len);
                  p.renderer.add_token(p.renderer.data, RULE);
                  p.renderer.end_token(p.renderer.data);
                  clear_root_pending(p);
                  p.hr_chars = 0;
                  continue;
              }
              p.hr_chars = 0;
            }
            if (p.pending[0] !== "_" && p.pending[1] === " ") {
              continue_or_add_list(p, LIST_UNORDERED);
              add_list_item(p, 2);
              parser_write(p, pending_with_char.slice(2));
              continue;
            }
            break;
          case "`":
            if (p.pending.length < 3) {
              if (char === "`") {
                p.pending = pending_with_char;
                p.fence_start = pending_with_char.length;
                continue;
              }
              p.fence_start = 0;
              break;
            }
            switch (char) {
              case "`":
                if (p.pending.length === p.fence_start) {
                  p.pending = pending_with_char;
                  p.fence_start = pending_with_char.length;
                } else {
                  add_token(p, PARAGRAPH);
                  clear_root_pending(p);
                  p.fence_start = 0;
                  parser_write(p, pending_with_char);
                }
                continue;
              case `
`: {
                end_tokens_to_indent(p, p.indent_len);
                add_token(p, CODE_FENCE);
                if (p.pending.length > p.fence_start) {
                  p.renderer.set_attr(p.renderer.data, LANG, p.pending.slice(p.fence_start));
                }
                clear_root_pending(p);
                p.token = NEWLINE;
                continue;
              }
              default:
                p.pending = pending_with_char;
                continue;
            }
          case "+":
            if (char !== " ")
              break;
            continue_or_add_list(p, LIST_UNORDERED);
            add_list_item(p, 2);
            continue;
          case "0":
          case "1":
          case "2":
          case "3":
          case "4":
          case "5":
          case "6":
          case "7":
          case "8":
          case "9":
            if (p.pending[p.pending.length - 1] === ".") {
              if (char !== " ")
                break;
              if (continue_or_add_list(p, LIST_ORDERED) && p.pending !== "1.") {
                p.renderer.set_attr(p.renderer.data, START, p.pending.slice(0, -1));
              }
              add_list_item(p, p.pending.length + 1);
              continue;
            } else {
              const char_code = char.charCodeAt(0);
              if (char_code === 46 || is_digit(char_code)) {
                p.pending = pending_with_char;
                continue;
              }
            }
            break;
          case "|":
            end_tokens_to_len(p, p.blockquote_idx);
            add_token(p, TABLE);
            add_token(p, TABLE_ROW);
            p.pending = "";
            parser_write(p, char);
            continue;
        }
        let to_write = pending_with_char;
        if (p.token === LINE_BREAK) {
          p.token = p.tokens[p.len];
          p.renderer.add_token(p.renderer.data, LINE_BREAK);
          p.renderer.end_token(p.renderer.data);
        } else if (p.indent_len >= 4) {
          let code_start = 0;
          for (;code_start < 4; code_start += 1) {
            if (p.indent[code_start] === "\t") {
              code_start = code_start + 1;
              break;
            }
          }
          to_write = p.indent.slice(code_start) + pending_with_char;
          add_token(p, CODE_BLOCK);
        } else {
          add_token(p, PARAGRAPH);
        }
        clear_root_pending(p);
        parser_write(p, to_write);
        continue;
      case TABLE:
        if (p.table_state === 1) {
          switch (char) {
            case "-":
            case " ":
            case "|":
            case ":":
              p.pending = pending_with_char;
              continue;
            case `
`:
              p.table_state = 2;
              p.pending = "";
              continue;
            default:
              end_token(p);
              p.table_state = 0;
              break;
          }
        } else {
          switch (p.pending) {
            case "|":
              add_token(p, TABLE_ROW);
              p.pending = "";
              parser_write(p, char);
              continue;
            case `
`:
              end_token(p);
              p.pending = "";
              p.table_state = 0;
              parser_write(p, char);
              continue;
          }
        }
        break;
      case TABLE_ROW:
        switch (p.pending) {
          case "":
            break;
          case "|":
            add_token(p, TABLE_CELL);
            end_token(p);
            p.pending = "";
            parser_write(p, char);
            continue;
          case `
`:
            end_token(p);
            p.table_state = Math.min(p.table_state + 1, 2);
            p.pending = "";
            parser_write(p, char);
            continue;
          default:
            add_token(p, TABLE_CELL);
            parser_write(p, char);
            continue;
        }
        break;
      case TABLE_CELL:
        if (p.pending === "|") {
          add_text(p);
          end_token(p);
          p.pending = "";
          parser_write(p, char);
          continue;
        }
        break;
      case CODE_BLOCK:
        switch (pending_with_char) {
          case `
    `:
          case `
   	`:
          case `
  	`:
          case `
 	`:
          case `
	`:
            p.text += `
`;
            p.pending = "";
            continue;
          case `
`:
          case `
 `:
          case `
  `:
          case `
   `:
            p.pending = pending_with_char;
            continue;
          default:
            if (p.pending.length !== 0) {
              add_text(p);
              end_token(p);
              p.pending = char;
            } else {
              p.text += char;
            }
            continue;
        }
      case CODE_FENCE:
        switch (char) {
          case "`":
            p.pending = pending_with_char;
            continue;
          case `
`:
            if (pending_with_char.length === p.fence_start + p.fence_end + 1) {
              add_text(p);
              end_token(p);
              p.pending = "";
              p.fence_start = 0;
              p.fence_end = 0;
              p.token = NEWLINE;
              continue;
            }
            p.token = NEWLINE;
            break;
          case " ":
            if (p.pending[0] === `
`) {
              p.pending = pending_with_char;
              p.fence_end += 1;
              continue;
            }
            break;
        }
        p.text += p.pending;
        p.pending = char;
        p.fence_end = 1;
        continue;
      case CODE_INLINE:
        switch (char) {
          case "`":
            if (pending_with_char.length === p.fence_start + Number(p.pending[0] === " ")) {
              add_text(p);
              end_token(p);
              p.pending = "";
              p.fence_start = 0;
            } else {
              p.pending = pending_with_char;
            }
            continue;
          case `
`:
            p.text += p.pending;
            p.pending = "";
            p.token = LINE_BREAK;
            p.blockquote_idx = 0;
            add_text(p);
            continue;
          case " ":
            p.text += p.pending;
            p.pending = char;
            continue;
          default:
            p.text += pending_with_char;
            p.pending = "";
            continue;
        }
      case MAYBE_TASK:
        switch (p.pending.length) {
          case 0:
            if (char !== "[")
              break;
            p.pending = pending_with_char;
            continue;
          case 1:
            if (char !== " " && char !== "x")
              break;
            p.pending = pending_with_char;
            continue;
          case 2:
            if (char !== "]")
              break;
            p.pending = pending_with_char;
            continue;
          case 3:
            if (char !== " ")
              break;
            p.renderer.add_token(p.renderer.data, CHECKBOX);
            if (p.pending[1] === "x") {
              p.renderer.set_attr(p.renderer.data, CHECKED, "");
            }
            p.renderer.end_token(p.renderer.data);
            p.pending = " ";
            continue;
        }
        p.token = p.tokens[p.len];
        p.pending = "";
        parser_write(p, pending_with_char);
        continue;
      case STRONG_AST:
      case STRONG_UND: {
        let symbol = "*";
        let italic = ITALIC_AST;
        if (p.token === STRONG_UND) {
          symbol = "_";
          italic = ITALIC_UND;
        }
        if (symbol === p.pending) {
          add_text(p);
          if (symbol === char) {
            end_token(p);
            p.pending = "";
            continue;
          }
          add_token(p, italic);
          p.pending = char;
          continue;
        }
        break;
      }
      case ITALIC_AST:
      case ITALIC_UND: {
        let symbol = "*";
        let strong = STRONG_AST;
        if (p.token === ITALIC_UND) {
          symbol = "_";
          strong = STRONG_UND;
        }
        switch (p.pending) {
          case symbol:
            if (symbol === char) {
              if (p.tokens[p.len - 1] === strong) {
                p.pending = pending_with_char;
              } else {
                add_text(p);
                add_token(p, strong);
                p.pending = "";
              }
            } else {
              add_text(p);
              end_token(p);
              p.pending = char;
            }
            continue;
          case symbol + symbol:
            const italic = p.token;
            add_text(p);
            end_token(p);
            end_token(p);
            if (symbol !== char) {
              add_token(p, italic);
              p.pending = char;
            } else {
              p.pending = "";
            }
            continue;
        }
        break;
      }
      case STRIKE:
        if (pending_with_char === "~~") {
          add_text(p);
          end_token(p);
          p.pending = "";
          continue;
        }
        break;
      case MAYBE_EQ_BLOCK:
        if (char === `
`) {
          add_text(p);
          add_token(p, EQUATION_BLOCK);
          p.pending = "";
        } else {
          p.token = p.tokens[p.len];
          if (p.pending[0] === "\\") {
            p.text += "[";
          } else {
            p.text += "$$";
          }
          p.pending = "";
          parser_write(p, char);
        }
        continue;
      case EQUATION_BLOCK:
        if (pending_with_char === "\\]" || pending_with_char === "$$") {
          add_text(p);
          end_token(p);
          p.pending = "";
          continue;
        }
        break;
      case EQUATION_INLINE:
        if (pending_with_char === "\\)" || p.pending[0] === "$") {
          add_text(p);
          end_token(p);
          if (char === ")") {
            p.pending = "";
          } else {
            p.pending = char;
          }
          continue;
        }
        break;
      case MAYBE_URL:
        if (pending_with_char === "http://" || pending_with_char === "https://") {
          add_text(p);
          add_token(p, RAW_URL);
          p.pending = pending_with_char;
          p.text = pending_with_char;
        } else if ("http:/"[p.pending.length] === char || "https:/"[p.pending.length] === char) {
          p.pending = pending_with_char;
        } else {
          p.token = p.tokens[p.len];
          parser_write(p, char);
        }
        continue;
      case LINK:
      case IMAGE:
        if (p.pending === "]") {
          add_text(p);
          if (char === "(") {
            p.pending = pending_with_char;
          } else {
            end_token(p);
            p.pending = char;
          }
          continue;
        }
        if (p.pending[0] === "]" && p.pending[1] === "(") {
          if (char === ")") {
            const type = p.token === LINK ? HREF : SRC;
            const url = p.pending.slice(2);
            p.renderer.set_attr(p.renderer.data, type, url);
            end_token(p);
            p.pending = "";
          } else {
            p.pending += char;
          }
          continue;
        }
        break;
      case RAW_URL:
        if (char === " " || char === `
` || char === "\\") {
          p.renderer.set_attr(p.renderer.data, HREF, p.pending);
          add_text(p);
          end_token(p);
          p.pending = char;
        } else {
          p.text += char;
          p.pending = pending_with_char;
        }
        continue;
      case MAYBE_BR:
        if (pending_with_char.startsWith("<br")) {
          if (pending_with_char.length === 3 || char === " " || char === "/" && (pending_with_char.length === 4 || p.pending[p.pending.length - 1] === " ")) {
            p.pending = pending_with_char;
            continue;
          }
          if (char === ">") {
            add_text(p);
            p.token = p.tokens[p.len];
            p.renderer.add_token(p.renderer.data, LINE_BREAK);
            p.renderer.end_token(p.renderer.data);
            p.pending = "";
            continue;
          }
        }
        p.token = p.tokens[p.len];
        p.text += "<";
        p.pending = p.pending.slice(1);
        parser_write(p, char);
        continue;
    }
    switch (p.pending[0]) {
      case "\\":
        if (p.token === IMAGE || p.token === EQUATION_BLOCK || p.token === EQUATION_INLINE)
          break;
        switch (char) {
          case "(":
            add_text(p);
            add_token(p, EQUATION_INLINE);
            p.pending = "";
            continue;
          case "[":
            p.token = MAYBE_EQ_BLOCK;
            p.pending = pending_with_char;
            continue;
          case `
`:
            p.pending = char;
            continue;
          default:
            let charcode = char.charCodeAt(0);
            p.pending = "";
            p.text += is_digit(charcode) || charcode >= 65 && charcode <= 90 || charcode >= 97 && charcode <= 122 ? pending_with_char : char;
            continue;
        }
      case `
`:
        switch (p.token) {
          case IMAGE:
          case EQUATION_BLOCK:
          case EQUATION_INLINE:
            break;
          case HEADING_1:
          case HEADING_2:
          case HEADING_3:
          case HEADING_4:
          case HEADING_5:
          case HEADING_6:
            add_text(p);
            end_tokens_to_len(p, p.blockquote_idx);
            p.blockquote_idx = 0;
            p.pending = char;
            continue;
          default:
            add_text(p);
            p.pending = char;
            p.token = LINE_BREAK;
            p.blockquote_idx = 0;
            continue;
        }
        break;
      case "<":
        if (p.token !== IMAGE && p.token !== EQUATION_BLOCK && p.token !== EQUATION_INLINE) {
          add_text(p);
          p.pending = pending_with_char;
          p.token = MAYBE_BR;
          continue;
        }
        break;
      case "`":
        if (p.token === IMAGE)
          break;
        if (char === "`") {
          p.fence_start += 1;
          p.pending = pending_with_char;
        } else {
          p.fence_start += 1;
          add_text(p);
          add_token(p, CODE_INLINE);
          p.text = char === " " || char === `
` ? "" : char;
          p.pending = "";
        }
        continue;
      case "_":
      case "*": {
        if (p.token === IMAGE || p.token === EQUATION_BLOCK || p.token === EQUATION_INLINE || p.token === STRONG_AST)
          break;
        let italic = ITALIC_AST;
        let strong = STRONG_AST;
        const symbol = p.pending[0];
        if (symbol === "_") {
          italic = ITALIC_UND;
          strong = STRONG_UND;
        }
        if (p.pending.length === 1) {
          if (symbol === char) {
            p.pending = pending_with_char;
            continue;
          }
          if (char !== " " && char !== `
`) {
            add_text(p);
            add_token(p, italic);
            p.pending = char;
            continue;
          }
        } else {
          if (symbol === char) {
            add_text(p);
            add_token(p, strong);
            add_token(p, italic);
            p.pending = "";
            continue;
          }
          if (char !== " " && char !== `
`) {
            add_text(p);
            add_token(p, strong);
            p.pending = char;
            continue;
          }
        }
        break;
      }
      case "~":
        if (p.token !== IMAGE && p.token !== STRIKE) {
          if (p.pending === "~") {
            if (char === "~") {
              p.pending = pending_with_char;
              continue;
            }
          } else {
            if (char !== " " && char !== `
`) {
              add_text(p);
              add_token(p, STRIKE);
              p.pending = char;
              continue;
            }
          }
        }
        break;
      case "$":
        if (p.token !== IMAGE && p.token !== STRIKE && p.pending === "$") {
          if (char === "$") {
            p.token = MAYBE_EQ_BLOCK;
            p.pending = pending_with_char;
            continue;
          } else if (is_delimeter_or_number(char.charCodeAt(0))) {
            break;
          } else {
            add_text(p);
            add_token(p, EQUATION_INLINE);
            p.pending = char;
            continue;
          }
        }
        break;
      case "[":
        if (p.token !== IMAGE && p.token !== LINK && p.token !== EQUATION_BLOCK && p.token !== EQUATION_INLINE && char !== "]") {
          add_text(p);
          add_token(p, LINK);
          p.pending = char;
          continue;
        }
        break;
      case "!":
        if (!(p.token === IMAGE) && char === "[") {
          add_text(p);
          add_token(p, IMAGE);
          p.pending = "";
          continue;
        }
        break;
      case " ":
        if (p.pending.length === 1 && char === " ") {
          continue;
        }
        break;
    }
    if (p.token !== IMAGE && p.token !== LINK && p.token !== EQUATION_BLOCK && p.token !== EQUATION_INLINE && char === "h" && (p.pending === " " || p.pending === "")) {
      p.text += p.pending;
      p.pending = char;
      p.token = MAYBE_URL;
      continue;
    }
    p.text += p.pending;
    p.pending = char;
  }
  add_text(p);
}
function default_renderer(root) {
  return {
    add_token: default_add_token,
    end_token: default_end_token,
    add_text: default_add_text,
    set_attr: default_set_attr,
    data: {
      nodes: [root, , , , ,],
      index: 0
    }
  };
}
function default_add_token(data, type) {
  let parent = data.nodes[data.index];
  let slot;
  switch (type) {
    case DOCUMENT:
      return;
    case BLOCKQUOTE:
      slot = document.createElement("blockquote");
      break;
    case PARAGRAPH:
      slot = document.createElement("p");
      break;
    case LINE_BREAK:
      slot = document.createElement("br");
      break;
    case RULE:
      slot = document.createElement("hr");
      break;
    case HEADING_1:
      slot = document.createElement("h1");
      break;
    case HEADING_2:
      slot = document.createElement("h2");
      break;
    case HEADING_3:
      slot = document.createElement("h3");
      break;
    case HEADING_4:
      slot = document.createElement("h4");
      break;
    case HEADING_5:
      slot = document.createElement("h5");
      break;
    case HEADING_6:
      slot = document.createElement("h6");
      break;
    case ITALIC_AST:
    case ITALIC_UND:
      slot = document.createElement("em");
      break;
    case STRONG_AST:
    case STRONG_UND:
      slot = document.createElement("strong");
      break;
    case STRIKE:
      slot = document.createElement("s");
      break;
    case CODE_INLINE:
      slot = document.createElement("code");
      break;
    case RAW_URL:
    case LINK:
      slot = document.createElement("a");
      break;
    case IMAGE:
      slot = document.createElement("img");
      break;
    case LIST_UNORDERED:
      slot = document.createElement("ul");
      break;
    case LIST_ORDERED:
      slot = document.createElement("ol");
      break;
    case LIST_ITEM:
      slot = document.createElement("li");
      break;
    case CHECKBOX:
      let checkbox = slot = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.disabled = true;
      break;
    case CODE_BLOCK:
    case CODE_FENCE:
      parent = parent.appendChild(document.createElement("pre"));
      slot = document.createElement("code");
      break;
    case TABLE:
      slot = document.createElement("table");
      break;
    case TABLE_ROW:
      switch (parent.children.length) {
        case 0:
          parent = parent.appendChild(document.createElement("thead"));
          break;
        case 1:
          parent = parent.appendChild(document.createElement("tbody"));
          break;
        default:
          parent = parent.children[1];
      }
      slot = document.createElement("tr");
      break;
    case TABLE_CELL:
      slot = document.createElement(parent.parentElement?.tagName === "THEAD" ? "th" : "td");
      break;
    case EQUATION_BLOCK:
      slot = document.createElement("equation-block");
      break;
    case EQUATION_INLINE:
      slot = document.createElement("equation-inline");
      break;
  }
  data.nodes[++data.index] = parent.appendChild(slot);
}
function default_end_token(data) {
  data.index -= 1;
}
function default_add_text(data, text) {
  data.nodes[data.index].appendChild(document.createTextNode(text));
}
function default_set_attr(data, type, value) {
  data.nodes[data.index].setAttribute(attr_to_html_attr(type), value);
}

// index.ts
class StreamingMarkdownElement extends HTMLElement {
  observer;
  container;
  shadowRootRef;
  parser = null;
  constructor() {
    super();
    this.shadowRootRef = this.attachShadow({ mode: "open" });
    const style = document.createElement("style");
    style.textContent = `
      :host { display: block; }
      :host([hidden]) { display: none; }
      .content { white-space: normal; }
      .content p { margin: 0 0 0.75rem 0; }
    `;
    this.container = document.createElement("div");
    this.container.className = "content";
    this.container.setAttribute("part", "content");
    this.shadowRootRef.append(style, this.container);
    this.observer = new MutationObserver(this.onMutations);
  }
  connectedCallback() {
    if (!this.hasAttribute("role"))
      this.setAttribute("role", "article");
    this.setAttribute("aria-live", "polite");
    this.parser = parser(default_renderer(this.container));
    this.observer.observe(this, { childList: true });
    this.consumeInitialChildren();
  }
  disconnectedCallback() {
    this.observer.disconnect();
    this.parser = null;
  }
  appendChunk(text) {
    if (!text)
      return;
    if (!this.parser) {
      this.parser = parser(default_renderer(this.container));
    }
    parser_write(this.parser, text);
    this.autoScrollIfNearBottom();
  }
  finish() {
    if (this.parser)
      parser_end(this.parser);
    this.setAttribute("aria-live", "off");
  }
  reset() {
    this.container.innerHTML = "";
    this.parser = parser(default_renderer(this.container));
    this.setAttribute("aria-live", "polite");
  }
  onMutations = (mutations) => {
    for (const m of mutations) {
      for (const node of Array.from(m.addedNodes)) {
        if (node.nodeType === Node.TEXT_NODE) {
          const text = node.data;
          if (text)
            this.appendChunk(text);
          node.parentNode?.removeChild(node);
        } else if (node.nodeType === Node.ELEMENT_NODE) {
          const text = node.textContent || "";
          if (text)
            this.appendChunk(text);
          node.parentNode?.removeChild(node);
        }
      }
    }
  };
  consumeInitialChildren() {
    const snapshot = Array.from(this.childNodes);
    for (const node of snapshot) {
      if (node.nodeType === Node.TEXT_NODE) {
        const text = node.data;
        if (text)
          this.appendChunk(text);
        node.parentNode?.removeChild(node);
      } else if (node.nodeType === Node.ELEMENT_NODE) {
        const text = node.textContent || "";
        if (text)
          this.appendChunk(text);
        node.parentNode?.removeChild(node);
      }
    }
  }
  autoScrollIfNearBottom() {
    const host = this;
    const nearBottom = host.scrollHeight - host.scrollTop - host.clientHeight < 64;
    if (nearBottom)
      host.scrollTop = host.scrollHeight;
  }
}
if (!customElements.get("streaming-md")) {
  customElements.define("streaming-md", StreamingMarkdownElement);
}
export {
  StreamingMarkdownElement
};
