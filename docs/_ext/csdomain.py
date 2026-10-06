"""A small C# domain for Sphinx.

Sphinx ships domains for Python, C, C++ and JavaScript, not C#. This one is just enough for the API
reference: object directives that show a C# signature as written and register an anchor and an
index entry, and cross-reference roles that resolve a full or partial dotted name.

    .. cs:namespace:: IronstrikeApi.Gameplay

    .. cs:class:: public static class Stats
       :fullname: IronstrikeApi.Gameplay.Stats

       .. cs:method:: public static IDisposable Modify(SkillCalcType stat, StatModifier modifier, int order = 0)
          :fullname: IronstrikeApi.Gameplay.Stats.Modify

    See :cs:meth:`~IronstrikeApi.Gameplay.Stats.Modify` or :cs:type:`Stats`.
"""

import re

from docutils import nodes
from docutils.parsers.rst import directives
from sphinx import addnodes
from sphinx.directives import ObjectDescription
from sphinx.domains import Domain, ObjType
from sphinx.roles import XRefRole
from sphinx.util.docutils import SphinxDirective
from sphinx.util.docfields import Field, GroupedField
from sphinx.util.nodes import make_refnode

KEYWORDS = {
    "public", "static", "sealed", "abstract", "virtual", "override", "readonly", "const", "event",
    "class", "struct", "enum", "interface", "delegate", "ref", "params",
}

# Readable labels for the general index.
LABELS = {
    "class": "class", "struct": "struct", "enum": "enum", "interface": "interface",
    "delegate": "delegate", "method": "method", "property": "property", "field": "field",
    "event": "event",
}


class CsNamespace(SphinxDirective):
    """Sets the namespace that following directives and roles are relative to."""

    required_arguments = 1
    has_content = False

    def run(self):
        self.env.ref_context["cs:namespace"] = self.arguments[0].strip()
        return []


class CsObject(ObjectDescription):
    # ":param x:" and ":returns:" render as the Python docs' "Parameters:" and "Returns:" lists.
    doc_field_types = [
        GroupedField("parameter", label="Parameters", names=("param", "parameter", "arg"), can_collapse=False),
        Field("returnvalue", label="Returns", has_arg=False, names=("returns", "return")),
    ]
    has_content = True
    required_arguments = 1
    final_argument_whitespace = True
    option_spec = {
        "fullname": directives.unchanged,
        "no-index": directives.flag,
        "noindex": directives.flag,
    }

    def _fullname(self, sig):
        full = self.options.get("fullname")
        if full:
            return full.strip()
        # Fall back to the identifier before the parameter list (or the last word).
        head = sig.split("(")[0].split("=")[0].split("{")[0].strip()
        name = head.split()[-1] if head else sig
        ns = self.env.ref_context.get("cs:namespace", "")
        return f"{ns}.{name}" if ns else name

    def handle_signature(self, sig, signode):
        full = self._fullname(sig)
        short = full.rsplit(".", 1)[-1]
        if short == "#ctor":
            short = full.rsplit(".", 2)[-2]

        # Split the written signature around the member's own name: modifiers and types before it,
        # parameters, accessors and values after it.
        # The name is the last match before the parameter list, accessors or value: in
        # "public Window Window { get; }" the first "Window" is the type.
        head_end = min([i for i in (sig.find("("), sig.find("{"), sig.find(" = "), sig.find(" : ")) if i >= 0] or [len(sig)])
        matches = list(re.finditer(r"(?<![\w.])" + re.escape(short) + r"(?![\w])", sig[:head_end]))
        m = matches[-1] if matches else None
        if m:
            before, after = sig[: m.start()], sig[m.end():]
        else:
            before, after = "", sig

        for tok in re.findall(r"\S+|\s+", before):
            if tok.isspace():
                signode += addnodes.desc_sig_space()
            elif tok in KEYWORDS:
                signode += addnodes.desc_sig_keyword(tok, tok)
            else:
                signode += addnodes.desc_type(tok, tok)
        signode += addnodes.desc_name(short, short)
        if after:
            signode += addnodes.desc_sig_element(after, after) if hasattr(addnodes, "desc_sig_element") \
                else nodes.Text(after)
        return full

    def add_target_and_index(self, name, sig, signode):
        anchor = "cs-" + name.replace("#", "")
        if anchor not in self.state.document.ids:
            signode["names"].append(anchor)
            signode["ids"].append(anchor)
            self.state.document.note_explicit_target(signode)
        domain = self.env.get_domain("cs")
        domain.note_object(name, self.objtype, anchor, self.env.docname)
        if "no-index" not in self.options and "noindex" not in self.options:
            parent, _, short = name.rpartition(".")
            if short == "#ctor":
                parent, _, short = parent.rpartition(".")
                label = f"{short} (constructor)"
            else:
                label = f"{short} ({LABELS.get(self.objtype, self.objtype)} in {parent})"
            self.indexnode["entries"].append(("single", label, anchor, "", None))

    def before_content(self):
        # Members inside a type resolve relative to it.
        if self.objtype in ("class", "struct", "enum", "interface") and self.names:
            self.env.ref_context.setdefault("cs:types", []).append(self.names[-1])

    def after_content(self):
        if self.objtype in ("class", "struct", "enum", "interface") and self.names:
            stack = self.env.ref_context.get("cs:types", [])
            if stack:
                stack.pop()


class CsXRefRole(XRefRole):
    def process_link(self, env, refnode, has_explicit_title, title, target):
        refnode["cs:namespace"] = env.ref_context.get("cs:namespace")
        stack = env.ref_context.get("cs:types") or []
        refnode["cs:type"] = stack[-1] if stack else None
        if not has_explicit_title:
            title = title.lstrip(".")
            if title.startswith("~"):
                title = title[1:].rsplit(".", 1)[-1]
            if self.reftype in ("meth",) and not title.endswith(")"):
                title += "()"
        if target.startswith("~"):
            target = target[1:]
        return title, target


class CsDomain(Domain):
    name = "cs"
    label = "C#"
    object_types = {
        "class": ObjType("class", "type", "obj"),
        "struct": ObjType("struct", "type", "obj"),
        "enum": ObjType("enum", "type", "obj"),
        "interface": ObjType("interface", "type", "obj"),
        "delegate": ObjType("delegate", "type", "obj"),
        "method": ObjType("method", "meth", "obj"),
        "property": ObjType("property", "prop", "obj"),
        "field": ObjType("field", "field", "obj"),
        "event": ObjType("event", "event", "obj"),
    }
    directives = {k: CsObject for k in object_types}
    directives["namespace"] = CsNamespace
    roles = {r: CsXRefRole() for r in ("type", "meth", "prop", "field", "event", "obj")}
    initial_data = {"objects": {}}

    @property
    def objects(self):
        return self.data.setdefault("objects", {})

    def note_object(self, name, objtype, anchor, docname):
        # Overloads share a name; the first one is the link target.
        self.objects.setdefault(name, (docname, anchor, objtype))

    def clear_doc(self, docname):
        for k, v in list(self.objects.items()):
            if v[0] == docname:
                del self.objects[k]

    def merge_domaindata(self, docnames, otherdata):
        for k, v in otherdata["objects"].items():
            if v[0] in docnames:
                self.objects.setdefault(k, v)

    def _find(self, target, node):
        if target in self.objects:
            return target
        # Relative to the current type, then the current namespace, then any unique suffix match.
        for prefix in (node.get("cs:type"), node.get("cs:namespace")):
            if prefix and f"{prefix}.{target}" in self.objects:
                return f"{prefix}.{target}"
        hits = [k for k in self.objects if k.endswith("." + target)]
        return hits[0] if len(hits) == 1 else None

    def resolve_xref(self, env, fromdocname, builder, typ, target, node, contnode):
        name = self._find(target, node)
        if name is None:
            return None
        docname, anchor, _ = self.objects[name]
        return make_refnode(builder, fromdocname, docname, anchor, contnode, name)

    def resolve_any_xref(self, env, fromdocname, builder, target, node, contnode):
        name = self._find(target, node)
        if name is None:
            return []
        docname, anchor, objtype = self.objects[name]
        role = self.object_types[objtype].roles[0]
        return [("cs:" + role, make_refnode(builder, fromdocname, docname, anchor, contnode, name))]

    def get_objects(self):
        for name, (docname, anchor, objtype) in self.objects.items():
            yield name, name, objtype, docname, anchor, 1


def setup(app):
    app.add_domain(CsDomain)
    return {"version": "1.0", "parallel_read_safe": True, "parallel_write_safe": True}
