#!/usr/bin/perl
# -----------------------------------------------------------------------------
#  version.pl — the ONE versioner, identical in every Hawkynt repo.
#
#  Model: FILES drive versions, never git tags. Each package's BASE version is
#  the NEAREST declaration — whatever is in place, first hit wins:
#    1. the manifest's own version field
#    2. the nearest ANCESTOR Directory.Build.props/.targets <Version>
#       (MSBuild inheritance, .NET only)
#    3. the repo-root VERSION file
#  The BUILD number is the commit count of the DECLARING file's PARENT FOLDER
#  (recursive) — so a version advances by how much the directory that declares
#  it actually changed; a repo-root declaration gets the whole-repo count.
#
#  This is what makes multi-package repos work: two NuGet packages in their own
#  folders get DIFFERENT build numbers, each reflecting only its own churn. An
#  untouched folder composes the IDENTICAL version on the next release, so
#  `dotnet nuget push --skip-duplicate` re-uses the already-published package
#  instead of re-uploading it (C--FrameworkExtensions relies on this heavily).
#  Repos that centralise the version in a props/VERSION file inherit the
#  declaring folder's coarser count — every commit below it bumps all heirs.
#
#  The REPO-level marker is NOT produced here: releases and nightlies are tagged
#  vYYYYMMDD / nightly-YYYYMMDD by the workflows. This script only answers "what
#  version does this package carry".
#
#  Version sources (kind -> file -> field):
#    dotnet : *.csproj/*.fsproj/*.vbproj, Directory.Build.props/.targets  <Version>
#    node   : package.json                                                "version"
#    php    : composer.json                                               "version"
#    perl   : *.pm that declares $VERSION                                 $VERSION
#    rust   : Cargo.toml                                        [package] version
#    cpp    : CMakeLists.txt                                    project(.. VERSION ..)
#    basic  : *.SUB/*.BAS/*.BI     %<PREFIX>_VERSION_MAJOR/_MINOR/_PATCH constants
#    file   : repo-root VERSION                            the whole file (READ-ONLY)
#
#  Composition respects each grammar. SemVer ecosystems (node, php, rust) cannot
#  take a 4th numeric part, so the build lands in build metadata: X.Y.Z+BUILD.
#  Everything else composes X.Y.Z.BUILD.
#
#  The root VERSION file is READ but never rewritten — it is the human-authored
#  declaration, and no build step consumes a stamped copy of it (those repos ask
#  for the composed string with a bare `version.pl` instead).
#
#  `--stamp` is a ONE-SHOT operation on a fresh CI checkout, which is how every
#  workflow uses it. dotnet/cpp/basic re-read cleanly and are idempotent; a perl
#  $VERSION whose base has fewer than three components (e.g. '1.00') gains one on
#  each stamp, so do not stamp a working tree twice and commit the result.
#
#  Two integration styles, both supported:
#    * STAMP (files drive): `--stamp` rewrites the version in every source file;
#      the build then packs straight from those files.
#    * COMPUTE-AND-PASS (one coordinated version): a repo that centralises its
#      version in Directory.Build.props/VERSION runs bare `version.pl` to get a
#      single string and passes it via `-p:Version=...` to every pack/publish.
#
#  Usage:
#    perl version.pl            # print the repo's single version  (X.Y.Z.BUILD)
#    perl version.pl --base     # print just the base              (X.Y.Z)
#    perl version.pl --build    # print just the build number      (commit count)
#    perl version.pl --stamp    # rewrite the version in every DECLARING file
#                               # (heirs pick the stamped props value up
#                               #  through MSBuild — inheritance stays intact)
#    perl version.pl --list     # print "<file>\t<effective-version>" per source,
#                               # inherited ones annotated with their declaring file
#
#  The single-version modes use the PRIMARY source: a root VERSION file, else the
#  shallowest Directory.Build.props with a <Version>, else the shallowest manifest
#  of any kind. Their build number is that primary source's parent-folder count.
#
#  Exit: 0 success, 2 bad usage.
# -----------------------------------------------------------------------------
use strict;
use warnings;
use FindBin;
use File::Find;
use File::Copy;

my $mode = $ARGV[0] // '';
exit 2 unless $mode eq '' || $mode eq '--base' || $mode eq '--build'
           || $mode eq '--stamp' || $mode eq '--list';

my $root = _RepoRoot("$FindBin::Bin");

# ---- single-version modes (compute-and-pass repos) -------------------------
if ($mode eq '' || $mode eq '--base' || $mode eq '--build') {
    my ($base, $dir, $kind) = _PrimarySource($root);
    if ($mode eq '--build') {
        print _BuildNumber($root, $dir), "\n";   # $dir undef -> repo-wide
        exit 0;
    }
    die "version.pl: no version source (VERSION / Directory.Build.props / project manifest)\n"
        unless defined $base;
    print(($mode eq '--base') ? $base
                              : _Compose($kind, $base, _BuildNumber($root, $dir)), "\n");
    exit 0;
}

# ---- per-source modes (stamp / list) ---------------------------------------
my @manifests = _Manifests($root);

if ($mode eq '--list') {
    for my $m (@manifests) {
        my ($file, $kind) = @$m;
        my $base = _ReadBase($kind, $file);
        if (defined $base) {
            print "$file\t" . _Compose($kind, $base, _BuildNumber($root, _ParentDir($root, $file))) . "\n";
            next;
        }
        # .NET projects may INHERIT their version (nearest ancestor
        # Directory.Build.props/.targets, else the repo-root VERSION file).
        # Show the EFFECTIVE version so no shipping project is invisible here;
        # the build number is the DECLARING file's folder count.
        next unless $kind eq 'dotnet' && $file =~ /\.(?:csproj|fsproj|vbproj)$/i;
        my ($ibase, $idir, $isrc) = _InheritedBase($root, $file);
        next unless defined $ibase;
        print "$file\t" . _Compose('dotnet', $ibase, _BuildNumber($root, $idir)) . "\t(inherited from $isrc)\n";
    }
    exit 0;
}

# --stamp
my $n = 0;
for my $m (@manifests) {
    my ($file, $kind) = @$m;
    my $base = _ReadBase($kind, $file);
    next unless defined $base;
    my $full = _Compose($kind, $base, _BuildNumber($root, _ParentDir($root, $file)));
    $n++ if _Rewrite($kind, $file, $full);
}
print "stamped $n source file(s) with per-folder build numbers\n";
exit 0;

# --------------------------------------------------------------------------- #

# SemVer ecosystems reject a 4th numeric component, so the build number goes
# into build metadata (X.Y.Z+BUILD) instead of a 4th field (X.Y.Z.BUILD).
# Deliberately a sub, not a file-scope `my` hash: the single-version modes exit
# before a file-scope initialiser further down would ever run, which would leave
# the table empty and silently compose SemVer versions the wrong way.
sub _IsSemVer {
    my ($k) = @_;
    return 0 unless defined $k;
    return ($k eq 'node' || $k eq 'php' || $k eq 'rust') ? 1 : 0;
}

sub _Compose {
    my ($kind, $base, $build) = @_;
    return undef unless defined $base;
    my @p = split /\./, $base;
    @p = @p[0 .. 2] if @p > 3;
    my $core = join('.', @p);
    return _IsSemVer($kind) ? "$core+$build" : "$core.$build";
}

sub _Kind {
    my ($f) = @_;
    return 'dotnet' if $f =~ /\.(?:csproj|fsproj|vbproj)$/i;
    return 'dotnet' if $f =~ m{(?:^|[/\\])Directory\.Build\.(?:props|targets)$}i;
    return 'node'   if $f =~ m{(?:^|[/\\])package\.json$}i;
    return 'php'    if $f =~ m{(?:^|[/\\])composer\.json$}i;
    return 'rust'   if $f =~ m{(?:^|[/\\])Cargo\.toml$}i;
    return 'cpp'    if $f =~ m{(?:^|[/\\])CMakeLists\.txt$}i;
    return 'perl'   if $f =~ /\.pm$/i;
    return 'basic'  if $f =~ /\.(?:sub|bas|bi)$/i;
    return 'file'   if $f =~ m{(?:^|[/\\])VERSION$};
    return undef;
}

sub _Manifests {
    my ($r) = @_;
    my @out;
    my %skip = map { $_ => 1 } qw(
        bin obj packages node_modules .git .vs .idea TestResults
        artifacts publish dist stage coverage vendor .svn target build cmake-build-debug
    );
    File::Find::find(
        {
            no_chdir   => 1,
            preprocess => sub { grep { !$skip{$_} } @_ },
            wanted     => sub {
                my $f = $File::Find::name;
                return unless -f $f;
                my $kind = _Kind($f);
                push @out, [$f, $kind] if $kind;
            },
        },
        $r,
    );
    return sort { $a->[0] cmp $b->[0] } @out;
}

# Nearest version declaration an MSBuild project INHERITS: walk from the
# project's folder up to the repo root looking for a Directory.Build.props
# (then .targets) with a <Version>; fall back to the repo-root VERSION file.
# Mirrors MSBuild's own lookup (only the NEAREST props auto-imports).
# Returns ($base, $declaringDirRelativeToRoot, $declaringFile) or ().
sub _InheritedBase {
    my ($root, $file) = @_;
    (my $r   = $root) =~ s{[/\\]$}{};
    (my $dir = $file) =~ s{[/\\][^/\\]+$}{};
    while (1) {
        for my $name ('Directory.Build.props', 'Directory.Build.targets') {
            my $cand = "$dir/$name";
            next unless -r $cand;
            my $b = _ReadDotnet($cand);
            return ($b, _ParentDir($root, $cand), $cand) if defined $b;
        }
        last if $dir eq $r || length($dir) <= length($r);
        last unless $dir =~ s{[/\\][^/\\]+$}{};
    }
    my $b = _VersionFile($root);
    return ($b, '', "$r/VERSION") if defined $b;
    return ();
}

# Primary source for the single repo version: VERSION, else the shallowest
# Directory.Build.props with a <Version>, else the shallowest manifest of any
# kind that declares one. Returns ($base, $parentDirRelativeToRoot, $kind).
sub _PrimarySource {
    my ($r) = @_;
    my $vf = "$r/VERSION";
    if (-r $vf) {
        my $b = _VersionFile($r);
        return ($b, '', 'file') if defined $b;
    }
    my (@props, @rest);
    for my $m (_Manifests($r)) {
        my ($file, $kind) = @$m;
        next if $kind eq 'file';                       # handled above
        next unless defined _ReadBase($kind, $file);
        if ($kind eq 'dotnet' && $file =~ m{Directory\.Build\.(?:props|targets)$}i) {
            push @props, [$file, $kind];
        } else {
            push @rest, [$file, $kind];
        }
    }
    my $depth = sub { my $d = _ParentDir($r, $_[0]); ($d eq '') ? 0 : ($d =~ tr{/\\}{}) + 1 };
    for my $list (\@props, \@rest) {
        next unless @$list;
        my ($best) = sort { $depth->($a->[0]) <=> $depth->($b->[0]) || $a->[0] cmp $b->[0] } @$list;
        return (_ReadBase($best->[1], $best->[0]), _ParentDir($r, $best->[0]), $best->[1]);
    }
    return (undef, undef, undef);
}

# ---- per-kind readers ------------------------------------------------------

sub _ReadBase {
    my ($kind, $f) = @_;
    return _ReadDotnet($f) if $kind eq 'dotnet';
    return _ReadJson($f)   if $kind eq 'node' || $kind eq 'php';
    return _ReadPerl($f)   if $kind eq 'perl';
    return _ReadRust($f)   if $kind eq 'rust';
    return _ReadCpp($f)    if $kind eq 'cpp';
    return _ReadBasic($f)  if $kind eq 'basic';
    return _ReadFile($f)   if $kind eq 'file';
    return undef;
}

sub _Slurp {
    my ($f) = @_;
    open my $fh, '<', $f or return undef;
    local $/;
    my $c = <$fh>;
    close $fh;
    return $c;
}

# Accepts an ALREADY-STAMPED four-component value too: _Compose keeps only the
# first three, so re-reading a stamped file yields the same base and stamping is
# idempotent. Capping at three here instead would make a stamped project
# unreadable, and `--list` after `--stamp` would silently omit it.
sub _ReadDotnet {
    my ($f) = @_;
    my $c = _Slurp($f) // return undef;
    return $c =~ m{<Version>\s*(\d+(?:\.\d+){0,3})\s*</Version>}i ? $1 : undef;
}

sub _ReadJson {
    my ($f) = @_;
    my $c = _Slurp($f) // return undef;
    return $c =~ m{"version"\s*:\s*"v?(\d+(?:\.\d+){0,2})}i ? $1 : undef;
}

sub _ReadPerl {
    my ($f) = @_;
    my $c = _Slurp($f) // return undef;
    return $c =~ m{\$VERSION\s*=\s*['"]v?(\d+(?:\.\d+){0,3})}i ? $1 : undef;
}

# Only the [package] table declares THIS crate's version — every [dependencies]
# entry carries its own `version` key, and a naive match would grab whichever
# came first in the file.
sub _ReadRust {
    my ($f) = @_;
    my $c = _Slurp($f) // return undef;
    return undef unless $c =~ m{^[ \t]*\[package\][ \t]*\r?$(.*?)(?=^[ \t]*\[|\z)}ms;
    my $pkg = $1;
    return $pkg =~ m{^[ \t]*version[ \t]*=[ \t]*"v?(\d+(?:\.\d+){0,2})}m ? $1 : undef;
}

# project(Name VERSION 1.2.3 LANGUAGES CXX) — CMake allows up to four numeric
# components, so a stamped X.Y.Z.BUILD stays valid input for the next read.
sub _ReadCpp {
    my ($f) = @_;
    my $c = _Slurp($f) // return undef;
    return $c =~ m{\bproject\s*\([^)]*?\bVERSION\s+(\d+(?:\.\d+){0,3})}is ? $1 : undef;
}

# QuickBASIC/PowerBASIC constants: %SVGA_VERSION_MAJOR = 1 (etc). MAJOR is
# required; MINOR/PATCH default to 0 so a partial declaration still versions.
sub _ReadBasic {
    my ($f) = @_;
    my $c = _Slurp($f) // return undef;
    return undef unless $c =~ m{%\w*VERSION_MAJOR\s*=\s*(\d+)}i;
    my $ma = $1;
    my $mi = ($c =~ m{%\w*VERSION_MINOR\s*=\s*(\d+)}i) ? $1 : 0;
    my $pa = ($c =~ m{%\w*VERSION_PATCH\s*=\s*(\d+)}i) ? $1 : 0;
    return "$ma.$mi.$pa";
}

sub _ReadFile {
    my ($f) = @_;
    my $c = _Slurp($f) // return undef;
    $c =~ s/^\s+|\s+$//g;
    return $c =~ m{^v?(\d+(?:\.\d+){0,3})} ? $1 : undef;
}

# ---- per-kind rewriters (return 1 if the file changed) ---------------------

sub _Rewrite {
    my ($kind, $f, $full) = @_;
    my $c = _Slurp($f);
    return 0 unless defined $c;
    my $orig = $c;
    if ($kind eq 'dotnet') {
        $c =~ s{<Version>\s*[\w.+\-]+\s*</Version>}{<Version>$full</Version>}ig;
    } elsif ($kind eq 'node' || $kind eq 'php') {
        $c =~ s{("version"\s*:\s*")[^"]*(")}{$1$full$2}i;   # first occurrence only
    } elsif ($kind eq 'perl') {
        $c =~ s{(\$VERSION\s*=\s*['"])[^'"]*(['"])}{$1$full$2};
    } elsif ($kind eq 'rust') {
        # Confine the rewrite to the [package] table (see _ReadRust).
        $c =~ s{(^[ \t]*\[package\][ \t]*\r?$)(.*?)(?=^[ \t]*\[|\z)}{
            my ($hdr, $body) = ($1, $2);
            $body =~ s{(^[ \t]*version[ \t]*=[ \t]*")[^"]*(")}{$1$full$2}m;
            $hdr . $body;
        }mse;
    } elsif ($kind eq 'cpp') {
        $c =~ s{(\bproject\s*\([^)]*?\bVERSION\s+)\d+(?:\.\d+){0,3}}{$1$full}is;
    } elsif ($kind eq 'basic') {
        # MAJOR/MINOR/PATCH are the human-authored base; only a dedicated BUILD
        # constant is machine-owned. Repos without one simply are not stamped.
        my ($build) = $full =~ m{(\d+)$};
        return 0 unless defined $build;
        $c =~ s{(%\w*VERSION_BUILD\s*=\s*)\d+}{$1$build}i;
    } else {
        # 'file' (root VERSION) is the human-authored declaration — never rewritten.
        return 0;
    }
    return 0 if $c eq $orig;
    my $tmp = "$f.\$\$\$";
    open my $out, '>', $tmp or die "write $tmp: $!";
    print $out $c;
    close $out;
    File::Copy::move($tmp, $f) or die "replace $f: $!";
    return 1;
}

# ---- git / path helpers ----------------------------------------------------

# The repo being versioned is the one in the WORKING DIRECTORY, not the one the
# script happens to live in: when this runs from the shared composite action the
# script sits in the action's own checkout, so walking up from $FindBin::Bin
# would resolve the action repo instead of the caller's. Ask git about the cwd
# first and only fall back to the script-relative walk (vendored copies, or a
# working directory that is not a git repo at all).
sub _RepoRoot {
    my ($d) = @_;
    my $top = `git rev-parse --show-toplevel 2>/dev/null`;
    chomp $top if defined $top;
    return $top if defined $top && length $top && -d "$top/.git";
    for (1 .. 20) {
        return $d if -d "$d/.git";
        my $p = $d;
        $p =~ s{[/\\][^/\\]+$}{};
        last if $p eq $d || $p eq '';
        $d = $p;
    }
    return $d;
}

sub _BuildNumber {
    my ($r, $rel) = @_;
    my $spec = (defined $rel && length $rel) ? " -- \"$rel\"" : "";
    my $c = `git -C "$r" rev-list --count HEAD$spec 2>&1`;
    chomp $c;
    return $c =~ /^\d+$/ ? $c : '0';
}

sub _VersionFile {
    my ($r) = @_;
    my $vf = "$r/VERSION";
    return undef unless -r $vf;
    return _ReadFile($vf);
}

# Path of a source file's directory, relative to the repo root ('' = repo root).
sub _ParentDir {
    my ($root, $file) = @_;
    (my $dir = $file) =~ s{[/\\][^/\\]+$}{};
    (my $r   = $root) =~ s{[/\\]$}{};
    $dir =~ s{^\Q$r\E[/\\]?}{};
    return $dir;
}
